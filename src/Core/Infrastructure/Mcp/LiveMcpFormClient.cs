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
/// Live MCP client that connects to the Safety MCP server over stdio.
///
/// Flow per incident:
///   1. Spawn the MCPServer subprocess via StdioClientTransport
///   2. StartNewIncident        → obtain a session ID
///   3. GetTotalPages           → know how many pages to fill
///   4. For each page:
///        GetPageComponents     → discover what fields the server wants
///        [AI extraction]       → ask the LLM to fill those fields from the incident context
///        UpdateIncidentPage    → submit the answers back to the server (validates required fields)
///   5. CompleteIncident        → seal the session
///   6. Return a FilledForm     → rendered in the UI Forms tab
///
/// Only the "safety" domain is handled here. Any other domain returns null,
/// causing HybridPatternRunner to fall back to structured text extraction.
/// Any exception during the MCP flow also returns null (safe degradation).
/// </summary>
public sealed class LiveMcpFormClient : IMcpFormClient
{
    private readonly ILogger<LiveMcpFormClient> _logger;
    private readonly IChatCompletionService     _chat;
    private readonly string                     _serverPath;

    /// <inheritdoc/>
    /// Always true — connection is attempted on each call and failures degrade gracefully.
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

    /// <inheritdoc/>
    public async Task<FilledForm?> TryFillFormAsync(
        string domain,
        string incidentContext,
        CancellationToken cancellationToken = default)
    {
        // Only safety is covered by the live server at this stage
        if (!domain.Equals("safety", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("[MCP] Domain '{Domain}' not yet supported by live client — falling back to text extraction", domain);
            return null;
        }

        try
        {
            return await FillSafetyFormAsync(incidentContext, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MCP] Live safety MCP call failed — falling back to text extraction");
            return null;
        }
    }

    // ── Main flow ─────────────────────────────────────────────────────────────

    private async Task<FilledForm?> FillSafetyFormAsync(
        string incidentContext, CancellationToken ct)
    {
        // Spawn the MCPServer process and connect via stdio
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name      = "SafetyMcpServer",
            Command   = "dotnet",
            // --no-build: MCPServer is pre-built by Web.csproj's BuildMcpServer target
            // before every Web build, so the binary is always up to date.
            Arguments = ["run", "--project", _serverPath, "--no-build"]
        });

        _logger.LogInformation("[MCP] Spawning Safety MCP server from '{Path}'", _serverPath);

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
        _logger.LogInformation("[MCP] Connected to Safety MCP server");

        // ── Discover what tool names the server actually registered ───────────
        // The SDK may convert PascalCase method names to camelCase (or keep them).
        // We build a case-insensitive lookup so our calls always resolve correctly
        // regardless of the naming convention the server uses.
        var registeredTools = await client.ListToolsAsync(cancellationToken: ct);
        var toolNames = registeredTools.ToDictionary(
            t => t.Name,
            t => t.Name,
            StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("[MCP] Server registered {Count} tool(s): {Names}",
            toolNames.Count, string.Join(", ", toolNames.Keys));

        // Resolves our PascalCase name to whatever the server actually registered
        string Tool(string name) =>
            toolNames.TryGetValue(name, out var actual) ? actual : name;

        // ── Step 1: Start a new session ───────────────────────────────────────
        var startResult = await client.CallToolAsync(
            Tool("StartNewIncident"),
            new Dictionary<string, object?>(),
            cancellationToken: ct);

        var incidentId = ExtractIncidentId(GetText(startResult));
        if (string.IsNullOrEmpty(incidentId))
        {
            _logger.LogWarning("[MCP] Could not extract incidentId from StartNewIncident response");
            return null;
        }
        _logger.LogInformation("[MCP] Session started — incidentId: {Id}", incidentId);

        // ── Step 2: How many pages does the form have? ────────────────────────
        var pagesResult  = await client.CallToolAsync(Tool("GetTotalPages"), new Dictionary<string, object?>(), cancellationToken: ct);
        var totalPages   = int.TryParse(GetText(pagesResult)?.Trim(), out var n) ? n : 2;
        _logger.LogInformation("[MCP] Form has {Total} page(s)", totalPages);

        // ── Step 3: Fill each page ────────────────────────────────────────────
        var allAnswers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var page = 1; page <= totalPages; page++)
        {
            _logger.LogInformation("[MCP] Processing page {Page}/{Total}", page, totalPages);

            // Ask the server what fields this page contains
            var componentsResult = await client.CallToolAsync(
                Tool("GetPageComponents"),
                new Dictionary<string, object?> { ["pageNumber"] = page },
                cancellationToken: ct);

            var fields = ParseFieldsFromComponentJson(GetText(componentsResult) ?? "{}");
            if (fields.Count == 0)
            {
                _logger.LogWarning("[MCP] No fields found for page {Page} — skipping", page);
                continue;
            }

            _logger.LogDebug("[MCP] Page {Page} fields: {Fields}",
                page, string.Join(", ", fields.Select(f => f.Name)));

            // Use the LLM to extract values for those fields from the incident description
            var answers = await AiExtractFieldValuesAsync(fields, incidentContext, ct);

            // All 8 fields across both pages are Required on the server side.
            // Guard against the AI missing any by falling back to a safe default.
            foreach (var field in fields.Where(f => f.Required))
            {
                if (!answers.TryGetValue(field.Name, out var v) || string.IsNullOrWhiteSpace(v))
                {
                    answers[field.Name] = field.Options?.FirstOrDefault() ?? "Unknown";
                    _logger.LogDebug("[MCP] Required field '{Field}' not extracted — using default", field.Name);
                }
            }

            // Submit the page back to the MCP server
            var updateResult = await client.CallToolAsync(
                Tool("UpdateIncidentPage"),
                new Dictionary<string, object?>
                {
                    ["incidentId"] = incidentId,
                    ["pageNumber"] = page,
                    ["answers"]    = answers
                },
                cancellationToken: ct);

            var ok = string.Equals(GetText(updateResult)?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("[MCP] Page {Page} submitted — server accepted: {Ok}", page, ok);

            foreach (var kv in answers)
                allAnswers[kv.Key] = kv.Value;
        }

        // ── Step 4: Seal the incident on the server ───────────────────────────
        await client.CallToolAsync(
            Tool("CompleteIncident"),
            new Dictionary<string, object?> { ["incidentId"] = incidentId },
            cancellationToken: ct);
        _logger.LogInformation("[MCP] Incident {Id} completed on server", incidentId);

        // ── Step 5: Build the FilledForm the UI will render ───────────────────
        return BuildFilledForm(allAnswers);
    }

    // ── AI extraction ─────────────────────────────────────────────────────────

    private async Task<Dictionary<string, string>> AiExtractFieldValuesAsync(
        List<FieldDef> fields, string incidentContext, CancellationToken ct)
    {
        var prompt = BuildExtractionPrompt(fields, incidentContext);

        var history = new ChatHistory();
        history.AddSystemMessage(
            "You are a form-filling assistant. " +
            "Extract values strictly from the incident description — do not invent information. " +
            "For selection fields, pick exactly one of the listed options.");
        history.AddUserMessage(prompt);

        var response = await _chat.GetChatMessageContentAsync(history, cancellationToken: ct);
        return ParseKeyValuePairs(response.Content ?? "", fields);
    }

    /// <summary>
    /// Builds a prompt that lists each field with its type and options,
    /// then asks the model to return "FieldName: value" pairs.
    /// Uses the same pattern as our proven FORM_FIELDS extraction so the
    /// parser doesn't need anything new.
    /// </summary>
    private static string BuildExtractionPrompt(List<FieldDef> fields, string incidentContext)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Extract values for each field below from the incident description.");
        sb.AppendLine("Write your answers as FieldName: value, one per line.");
        sb.AppendLine("For selection fields choose EXACTLY one of the listed options.");
        sb.AppendLine("If a value cannot be determined write: Unknown");
        sb.AppendLine();
        sb.AppendLine("INCIDENT:");
        sb.AppendLine(incidentContext);
        sb.AppendLine();
        sb.AppendLine("FORM_FIELDS:");
        foreach (var f in fields)
        {
            if (f.Options?.Length > 0)
                sb.AppendLine($"{f.Name}: (choose one: {string.Join(" | ", f.Options)})");
            else
                sb.AppendLine($"{f.Name}:");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parses "FieldName: value" pairs from the AI's response.
    /// Only accepts keys that appear in the known field set.
    /// Strips parenthetical notes the model sometimes appends.
    /// </summary>
    private static Dictionary<string, string> ParseKeyValuePairs(
        string response, List<FieldDef> fields)
    {
        var known  = new HashSet<string>(fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in response.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var key = line[..colon].Trim();
            var val = line[(colon + 1)..].Trim();

            if (!known.Contains(key) || string.IsNullOrWhiteSpace(val)) continue;

            // Strip parenthetical explanations: "Slip/Trip/Fall (because...)" → "Slip/Trip/Fall"
            var paren = val.IndexOf('(');
            if (paren > 0) val = val[..paren].Trim();
            val = val.TrimEnd('.', ',', ';');

            if (!string.IsNullOrWhiteSpace(val))
                result[key] = val;
        }

        return result;
    }

    // ── Component JSON parsing ────────────────────────────────────────────────

    /// <summary>
    /// Recursively walks the component JSON returned by GetPageComponents and
    /// extracts field definitions. The server returns PascalCase property names
    /// (default System.Text.Json serialisation with no custom naming policy).
    /// </summary>
    private static List<FieldDef> ParseFieldsFromComponentJson(string json)
    {
        var fields = new List<FieldDef>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            WalkComponentElement(doc.RootElement, fields);
        }
        catch (JsonException ex)
        {
            // Return whatever we managed to parse — caller handles empty list
            _ = ex;
        }
        return fields;
    }

    private static void WalkComponentElement(JsonElement el, List<FieldDef> fields)
    {
        if (el.ValueKind != JsonValueKind.Object) return;

        // A form input element has a FieldName property
        if (el.TryGetProperty("FieldName", out var fieldNameEl))
        {
            var name     = fieldNameEl.GetString() ?? "";
            var label    = el.TryGetProperty("Label",    out var lbl)  ? lbl.GetString()  ?? name : name;
            var type     = el.TryGetProperty("Type",     out var typ)  ? typ.GetString()  ?? ""   : "";
            var required = false;
            string[]? options = null;

            if (el.TryGetProperty("Validation", out var valEl) &&
                valEl.TryGetProperty("Required", out var reqEl))
                required = reqEl.GetBoolean();

            if (el.TryGetProperty("Options", out var optsEl) &&
                optsEl.ValueKind == JsonValueKind.Array)
            {
                options = optsEl.EnumerateArray()
                    .Select(o => o.GetString() ?? "")
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();
            }

            if (!string.IsNullOrEmpty(name))
                fields.Add(new FieldDef(name, label, type, options, required));
        }

        // Recurse into nested component arrays
        if (el.TryGetProperty("Components", out var compsEl) &&
            compsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in compsEl.EnumerateArray())
                WalkComponentElement(child, fields);
        }
    }

    // ── FilledForm construction ───────────────────────────────────────────────

    /// <summary>
    /// Maps the collected answers (keyed by MCP server field names) into a
    /// FilledForm that the UI Forms tab knows how to render.
    /// Field names and options mirror the MCPServer's PageDefinitions exactly.
    /// </summary>
    private static FilledForm BuildFilledForm(Dictionary<string, string> answers)
    {
        static FilledFormField MakeField(
            string id, string label, string type,
            string[]? options, Dictionary<string, string> answers)
        {
            var hasValue = answers.TryGetValue(id, out var v) && !string.IsNullOrEmpty(v);
            return new FilledFormField
            {
                Id       = id,
                Label    = label,
                Type     = type,
                Options  = options,
                Value    = hasValue ? v : null,
                AiFilled = hasValue
            };
        }

        return new FilledForm
        {
            Domain    = "safety",
            FormTitle = "Safety Incident Report",
            Pages     =
            [
                new FilledFormPage
                {
                    Number = 1,
                    Title  = "Incident Basic Information",
                    Fields =
                    [
                        MakeField("IncidentDate",     "Incident Date / Time", "text",   null, answers),
                        MakeField("IncidentLocation", "Location",             "text",   null, answers),
                        MakeField("IncidentType",     "Incident Type",        "select",
                            ["Slip/Trip/Fall", "Equipment Failure", "Chemical Spill",
                             "Fire/Explosion", "Electrical", "Vehicle Accident", "Other"], answers),
                        MakeField("SeverityLevel",    "Severity Level",       "select",
                            ["Minor (No injury, minimal damage)",
                             "Moderate (First aid required, some damage)",
                             "Serious (Medical attention required, significant damage)",
                             "Critical (Hospitalization, major damage or loss)"], answers),
                    ]
                },
                new FilledFormPage
                {
                    Number = 2,
                    Title  = "Incident Details and Actions",
                    Fields =
                    [
                        MakeField("PersonsInvolved",       "Persons Involved",          "textarea", null, answers),
                        MakeField("InjuriesReported",      "Injuries Reported",         "textarea", null, answers),
                        MakeField("IncidentDescription",   "Incident Description",      "textarea", null, answers),
                        MakeField("ImmediateActionsTaken", "Immediate Actions Taken",   "textarea", null, answers),
                    ]
                }
            ]
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns the first text-type content block from a tool result.</summary>
    private static string? GetText(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;

    /// <summary>
    /// Parses the incidentId from the JSON returned by StartNewIncident.
    /// The server returns the full SafetyIncident object serialised as JSON.
    /// </summary>
    private static string? ExtractIncidentId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("IncidentId", out var el))
                return el.GetString();
        }
        catch { /* ignored — return null below */ }
        return null;
    }

    // ── Internal model ────────────────────────────────────────────────────────

    private sealed record FieldDef(
        string    Name,
        string    Label,
        string    Type,
        string[]? Options,
        bool      Required);
}
