using System.Text;
using System.Text.Json;
using Core.Interfaces;
using Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Core.Infrastructure.Mcp;

/// <summary>
/// Live MCP client that fills the Safety form page by page over a single persistent
/// stdio connection.
///
/// The live forms are branching — a later page's questions depend on how earlier
/// pages were answered — so pages can't be fetched up front. Instead the runner drives
/// a session: fetch the current page → ask the user → submit the page → fetch the next.
/// The connection is held open for the whole session (including across the pauses while
/// the user answers) because every page is fetched from, and submitted to, the same
/// in-memory incident on the server.
///
/// Only the "safety" domain is handled; other domains return null so the runner falls
/// back to structured text extraction.
/// </summary>
public sealed class LiveMcpFormClient : IMcpFormClient
{
    private readonly ILogger<LiveMcpFormClient> _logger;
    private readonly IChatCompletionService     _chat;
    private readonly string                     _serverPath;

    /// <inheritdoc/>
    public bool IsConnected => true;

    /// <inheritdoc/>
    public IReadOnlyList<McpToolDefinition> ExpectedTools => McpToolSchemas.All;

    public LiveMcpFormClient(
        ILogger<LiveMcpFormClient> logger,
        Kernel kernel,
        IConfiguration configuration)
    {
        _logger     = logger;
        _chat       = kernel.GetRequiredService<IChatCompletionService>();
        _serverPath = configuration["Mcp:SafetyServerPath"] ?? "../MCPServer";
    }

    private static bool IsSafety(string domain) =>
        domain.Equals("safety", StringComparison.OrdinalIgnoreCase);

    // ── Open a page-by-page session ────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IMcpFormSession?> BeginSessionAsync(
        string domain, CancellationToken cancellationToken = default)
    {
        if (!IsSafety(domain))
        {
            _logger.LogDebug("[MCP] Domain '{Domain}' not MCP-backed — caller falls back", domain);
            return null;
        }

        try
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name      = "SafetyMcpServer",
                Command   = "dotnet",
                // --no-build: MCPServer is pre-built by Web.csproj's BuildMcpServer target.
                Arguments = ["run", "--project", _serverPath, "--no-build"]
            });

            _logger.LogInformation("[MCP] Spawning Safety MCP server from '{Path}'", _serverPath);
            var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            _logger.LogInformation("[MCP] Connected to Safety MCP server");

            // Resolve tool names (the SDK renames methods to snake_case)
            var registered = await client.ListToolsAsync(cancellationToken: cancellationToken);
            var map = registered.ToDictionary(t => Normalise(t.Name), t => t.Name);
            _logger.LogInformation(
                "[MCP] Server registered {Count} tool(s): {Names}",
                map.Count, string.Join(", ", registered.Select(t => t.Name)));
            string Tool(string name) => map.TryGetValue(Normalise(name), out var a) ? a : name;

            // Start a fresh incident — this session owns it for its lifetime
            var startResult = await client.CallToolAsync(
                Tool("StartNewIncident"), new Dictionary<string, object?>(),
                cancellationToken: cancellationToken);

            var incidentId = ExtractIncidentId(startResult);
            if (string.IsNullOrEmpty(incidentId))
            {
                _logger.LogWarning("[MCP] Could not start incident — closing session");
                await client.DisposeAsync();
                return null;
            }

            _logger.LogInformation("[MCP] Session started — incidentId: {Id}", incidentId);
            return new LiveMcpFormSession(client, Tool, _logger, incidentId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MCP] BeginSessionAsync failed — caller will fall back");
            return null;
        }
    }

    // ── AI pre-fill (per page) ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> ExtractAnswersAsync(
        string domain,
        IReadOnlyList<McpFormField> fields,
        string context,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafety(domain) || fields.Count == 0)
            return new Dictionary<string, string>();

        try
        {
            var history = new ChatHistory();
            history.AddSystemMessage(
                "You are a form-filling assistant. " +
                "Extract values strictly from the incident description — do not invent information. " +
                "For selection fields, pick exactly one of the listed options. " +
                "If a value genuinely cannot be determined, omit that line entirely.");
            history.AddUserMessage(BuildExtractionPrompt(fields, context));

            var response = await _chat.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);
            var answers  = ParseKeyValuePairs(response.Content ?? "", fields);

            _logger.LogInformation(
                "[MCP] AI pre-filled {Filled}/{Total} field(s) on this page", answers.Count, fields.Count);
            return answers;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MCP] ExtractAnswersAsync failed — no values pre-filled");
            return new Dictionary<string, string>();
        }
    }

    private static string Normalise(string name) =>
        name.Replace("_", "").Replace("-", "").ToLowerInvariant();

    // ── AI extraction helpers ──────────────────────────────────────────────────

    private static string BuildExtractionPrompt(IReadOnlyList<McpFormField> fields, string context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Extract values for each field below from the incident description.");
        sb.AppendLine("Write your answers as FieldName: value, one per line.");
        sb.AppendLine("For selection fields choose EXACTLY one of the listed options.");
        sb.AppendLine("If a value cannot be determined, omit that line completely.");
        sb.AppendLine();
        sb.AppendLine(context);
        sb.AppendLine();
        sb.AppendLine("FORM_FIELDS:");
        foreach (var f in fields)
        {
            if (f.IsChoice)
                sb.AppendLine($"{f.Id}: (choose one: {string.Join(" | ", f.Options!)})");
            else
                sb.AppendLine($"{f.Id}:");
        }
        return sb.ToString();
    }

    private static Dictionary<string, string> ParseKeyValuePairs(
        string response, IReadOnlyList<McpFormField> fields)
    {
        var known  = new HashSet<string>(fields.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in response.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var key = line[..colon].Trim();
            var val = line[(colon + 1)..].Trim();
            if (!known.Contains(key) || string.IsNullOrWhiteSpace(val)) continue;

            var paren = val.IndexOf('(');
            if (paren > 0) val = val[..paren].Trim();
            val = val.TrimEnd('.', ',', ';');

            if (!string.IsNullOrWhiteSpace(val) && !IsEmptyValue(val))
                result[key] = val;
        }

        return result;
    }

    private static bool IsEmptyValue(string v) =>
        v.Equals("unknown",       StringComparison.OrdinalIgnoreCase) ||
        v.Equals("n/a",           StringComparison.OrdinalIgnoreCase) ||
        v.Equals("not specified", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("not mentioned", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("none",          StringComparison.OrdinalIgnoreCase) ||
        v.StartsWith("[",         StringComparison.OrdinalIgnoreCase);

    // ── Component JSON parsing (shared by the session) ─────────────────────────

    /// <summary>Parses a page's title (first TitleDisplay) and input fields.</summary>
    private static (string Title, List<McpFormField> Fields) ParsePage(string json, int page)
    {
        var fields = new List<McpFormField>();
        var title  = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            title = FindTitle(doc.RootElement) ?? "";
            WalkComponentElement(doc.RootElement, fields, page);
        }
        catch (JsonException)
        {
            // Not valid JSON — caller handles the empty list (falls back / stops)
        }
        return (title, fields);
    }

    /// <summary>Finds the first TitleDisplay component's value (the page heading).</summary>
    private static string? FindTitle(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;

        if (TryGetProp(el, "Type", out var typeEl) &&
            typeEl.ValueKind == JsonValueKind.String &&
            string.Equals(typeEl.GetString(), "TitleDisplay", StringComparison.OrdinalIgnoreCase) &&
            TryGetProp(el, "Value", out var valEl) &&
            valEl.ValueKind == JsonValueKind.String)
            return valEl.GetString();

        if (TryGetProp(el, "Components", out var compsEl) &&
            compsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in compsEl.EnumerateArray())
            {
                var found = FindTitle(child);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static void WalkComponentElement(JsonElement el, List<McpFormField> fields, int page)
    {
        if (el.ValueKind != JsonValueKind.Object) return;

        if (TryGetProp(el, "FieldName", out var fieldNameEl) &&
            fieldNameEl.ValueKind == JsonValueKind.String)
        {
            var name  = fieldNameEl.GetString() ?? "";
            var label = TryGetProp(el, "Label", out var lbl) ? lbl.GetString() ?? name : name;
            var type  = TryGetProp(el, "Type",  out var typ) ? typ.GetString() ?? ""   : "";
            var required = false;
            string[]? options = null;

            if (TryGetProp(el, "Validation", out var valEl) &&
                TryGetProp(valEl, "Required", out var reqEl) &&
                (reqEl.ValueKind == JsonValueKind.True || reqEl.ValueKind == JsonValueKind.False))
                required = reqEl.GetBoolean();

            if (TryGetProp(el, "Options", out var optsEl) &&
                optsEl.ValueKind == JsonValueKind.Array)
            {
                options = optsEl.EnumerateArray()
                    .Select(o => o.GetString() ?? "")
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();
                if (options.Length == 0) options = null;
            }

            if (!string.IsNullOrEmpty(name))
                fields.Add(new McpFormField(page, name, label, NormaliseType(type, options), options, required));
        }

        if (TryGetProp(el, "Components", out var compsEl) &&
            compsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in compsEl.EnumerateArray())
                WalkComponentElement(child, fields, page);
        }
    }

    private static string NormaliseType(string serverType, string[]? options) =>
        serverType.ToLowerInvariant() switch
        {
            "radiobuttongroup"                => "radio",
            "datetimepicker"                  => "date",
            "textareainput"                   => "textarea",
            _ when options is { Length: > 0 } => "radio",
            _                                 => "text"
        };

    private static bool TryGetProp(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;

        var target = Normalise(name);
        foreach (var prop in obj.EnumerateObject())
        {
            if (Normalise(prop.Name) == target)
            {
                value = prop.Value;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Fallback field definitions for the safety form, used when the server's component
    /// JSON can't be parsed. Mirrors the MCPServer's PageDefinitions (static form only).
    /// </summary>
    private static List<McpFormField> KnownSafetyFields(int page) => page switch
    {
        1 =>
        [
            new(1, "IncidentDate",     "When did the incident occur?",      "date",  null, true),
            new(1, "IncidentLocation", "Where did the incident occur?",     "text",  null, true),
            new(1, "IncidentType",     "What type of incident was it?",     "radio",
                ["Slip/Trip/Fall", "Equipment Failure", "Chemical Spill",
                 "Fire/Explosion", "Electrical", "Vehicle Accident", "Other"], true),
            new(1, "SeverityLevel",    "What is the severity level of the incident?", "radio",
                ["Minor (No injury, minimal damage)",
                 "Moderate (First aid required, some damage)",
                 "Serious (Medical attention required, significant damage)",
                 "Critical (Hospitalization, major damage or loss)"], true),
        ],
        2 =>
        [
            new(2, "PersonsInvolved",       "Who was involved in or witnessed the incident?", "textarea", null, true),
            new(2, "InjuriesReported",      "Were there any injuries reported? If yes, please describe.", "textarea", null, true),
            new(2, "IncidentDescription",   "Please provide a detailed description of what happened.", "textarea", null, true),
            new(2, "ImmediateActionsTaken", "What immediate actions were taken following the incident?", "textarea", null, true),
        ],
        _ => []
    };

    // ── FilledForm construction (from the pages actually served) ────────────────

    private static FilledForm BuildFilledForm(
        IReadOnlyList<McpFormPage> pages, IReadOnlyDictionary<string, string> answers)
    {
        var filledPages = pages.Select(p => new FilledFormPage
        {
            Number = p.Number,
            Title  = string.IsNullOrWhiteSpace(p.Title) ? $"Page {p.Number}" : p.Title,
            Fields = p.Fields.Select(f =>
            {
                var hasValue = answers.TryGetValue(f.Id, out var v) && !string.IsNullOrEmpty(v);
                return new FilledFormField
                {
                    Id       = f.Id,
                    Label    = f.Label,
                    Type     = UiType(f),
                    Options  = f.Options,
                    Value    = hasValue ? v : null,
                    AiFilled = hasValue
                };
            }).ToList()
        }).ToList();

        return new FilledForm
        {
            Domain    = "safety",
            FormTitle = "Safety Incident Report",
            Pages     = filledPages
        };
    }

    /// <summary>Maps a field to the UI field type the Forms renderer understands.</summary>
    private static string UiType(McpFormField f) =>
        f.IsChoice ? "select" : f.Type == "textarea" ? "textarea" : "text";

    // ── Tool-result helpers ────────────────────────────────────────────────────

    private static string? GetText(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;

    private static string? ExtractIncidentId(CallToolResult result)
    {
        if (result.StructuredContent is JsonElement structured &&
            TryFindStringProperty(structured, "incidentId", out var fromStructured))
            return fromStructured;

        var json = GetText(result);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (TryFindStringProperty(doc.RootElement, "incidentId", out var fromText))
                return fromText;
        }
        catch { /* not JSON — return null below */ }
        return null;
    }

    private static bool TryFindStringProperty(JsonElement obj, string name, out string? value)
    {
        value = null;
        if (obj.ValueKind != JsonValueKind.Object) return false;

        var target = Normalise(name);
        foreach (var prop in obj.EnumerateObject())
        {
            if (Normalise(prop.Name) == target && prop.Value.ValueKind == JsonValueKind.String)
            {
                value = prop.Value.GetString();
                return !string.IsNullOrEmpty(value);
            }
        }
        return false;
    }

    // ── The stateful session ───────────────────────────────────────────────────

    /// <summary>
    /// Holds one persistent MCP connection and drives the page-by-page flow against a
    /// single server-side incident. Tracks the pages it served so it can build the
    /// final UI form to match exactly what the server presented.
    /// </summary>
    private sealed class LiveMcpFormSession : IMcpFormSession
    {
        private readonly McpClient            _client;
        private readonly Func<string, string> _tool;
        private readonly ILogger              _logger;
        private readonly List<McpFormPage>    _served = [];

        public string IncidentId { get; }

        public LiveMcpFormSession(McpClient client, Func<string, string> tool, ILogger logger, string incidentId)
        {
            _client     = client;
            _tool       = tool;
            _logger     = logger;
            IncidentId  = incidentId;
        }

        public async Task<McpFormPage?> GetPageAsync(int pageNumber, CancellationToken ct = default)
        {
            var result = await _client.CallToolAsync(
                _tool("GetPageComponents"),
                new Dictionary<string, object?> { ["pageNumber"] = pageNumber },
                cancellationToken: ct);

            var json = GetText(result) ?? "";
            _logger.LogDebug("[MCP] Page {Page} raw components JSON: {Json}", pageNumber, json);

            // No such page → no more pages (terminates the loop for static and branching forms)
            if (string.IsNullOrWhiteSpace(json) || json.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                return null;

            var (title, fields) = ParsePage(json, pageNumber);
            if (fields.Count == 0)
            {
                // Static-form safety net (today's server). Empty here means no more pages.
                fields = KnownSafetyFields(pageNumber);
                if (fields.Count == 0) return null;
                _logger.LogWarning("[MCP] No fields parsed for page {Page} — using known safety schema", pageNumber);
            }

            var page = new McpFormPage(pageNumber, title, fields);
            _served.Add(page);
            _logger.LogInformation(
                "[MCP] Page {Page} '{Title}' — {Count} field(s)", pageNumber, page.Title, fields.Count);
            return page;
        }

        public async Task<bool> SubmitPageAsync(
            int pageNumber, IReadOnlyDictionary<string, string> answers, CancellationToken ct = default)
        {
            var result = await _client.CallToolAsync(
                _tool("UpdateIncidentPage"),
                new Dictionary<string, object?>
                {
                    ["incidentId"] = IncidentId,
                    ["pageNumber"] = pageNumber,
                    ["answers"]    = new Dictionary<string, string>(answers)
                },
                cancellationToken: ct);

            var ok = string.Equals(GetText(result)?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("[MCP] Page {Page} submitted — server accepted: {Ok}", pageNumber, ok);
            return ok;
        }

        public async Task CompleteAsync(CancellationToken ct = default)
        {
            await _client.CallToolAsync(
                _tool("CompleteIncident"),
                new Dictionary<string, object?> { ["incidentId"] = IncidentId },
                cancellationToken: ct);
            _logger.LogInformation("[MCP] Incident {Id} completed on server", IncidentId);
        }

        public FilledForm BuildForm(IReadOnlyDictionary<string, string> answers) =>
            BuildFilledForm(_served, answers);

        public async ValueTask DisposeAsync()
        {
            try { await _client.DisposeAsync(); }
            catch { /* best-effort — the child process is torn down on dispose */ }
        }
    }
}
