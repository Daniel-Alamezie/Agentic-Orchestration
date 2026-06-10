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
/// The form fill is split into three steps so the runner can pause between them
/// to ask the user for missing details (a conversational, form-driven flow):
///
///   GetFormFieldsAsync  — connect → get_total_pages → get_page_components per page
///                         → return the field definitions discovered from the server.
///   ExtractAnswersAsync — ask the LLM to pre-fill what it can from the incident text.
///   SubmitFormAsync     — connect → start_new_incident → update_incident_page per page
///                         → complete_incident → return the finished FilledForm.
///
/// Each step opens its own short-lived connection (the server keeps incident state
/// in-memory per process, so a single submission connection does start→fill→complete
/// in one go). Only the "safety" domain is handled; other domains return empty/null
/// so the runner falls back to structured text extraction.
/// </summary>
public sealed class LiveMcpFormClient : IMcpFormClient
{
    private readonly ILogger<LiveMcpFormClient> _logger;
    private readonly IChatCompletionService     _chat;
    private readonly string                     _serverPath;

    /// <inheritdoc/>
    /// Always true — connections are attempted per call and failures degrade gracefully.
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

    // ── Step 1: Discover the form's fields ─────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<McpFormField>> GetFormFieldsAsync(
        string domain, CancellationToken cancellationToken = default)
    {
        if (!IsSafety(domain))
        {
            _logger.LogDebug("[MCP] Domain '{Domain}' not MCP-backed — caller falls back", domain);
            return [];
        }

        try
        {
            return await WithClientAsync(async (client, tool) =>
            {
                var pagesResult = await client.CallToolAsync(
                    tool("GetTotalPages"), new Dictionary<string, object?>(),
                    cancellationToken: cancellationToken);
                var totalPages = int.TryParse(GetText(pagesResult)?.Trim(), out var n) ? n : 2;
                _logger.LogInformation("[MCP] Form has {Total} page(s)", totalPages);

                var fields = new List<McpFormField>();
                for (var page = 1; page <= totalPages; page++)
                {
                    var componentsResult = await client.CallToolAsync(
                        tool("GetPageComponents"),
                        new Dictionary<string, object?> { ["pageNumber"] = page },
                        cancellationToken: cancellationToken);

                    var json = GetText(componentsResult) ?? "{}";
                    _logger.LogDebug("[MCP] Page {Page} raw components JSON: {Json}", page, json);

                    var pageFields = ParseFieldsFromComponentJson(json, page);
                    if (pageFields.Count == 0)
                    {
                        _logger.LogWarning(
                            "[MCP] No fields parsed for page {Page} — using known safety schema", page);
                        pageFields = KnownSafetyFields(page);
                    }
                    fields.AddRange(pageFields);
                }

                _logger.LogInformation(
                    "[MCP] Discovered {Count} field(s): {Names}",
                    fields.Count, string.Join(", ", fields.Select(f => f.Id)));
                return (IReadOnlyList<McpFormField>)fields;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MCP] GetFormFieldsAsync failed — caller will fall back");
            return [];
        }
    }

    // ── Step 2: AI pre-fill ────────────────────────────────────────────────────

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
                "[MCP] AI pre-filled {Filled}/{Total} field(s)", answers.Count, fields.Count);
            return answers;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MCP] ExtractAnswersAsync failed — no values pre-filled");
            return new Dictionary<string, string>();
        }
    }

    // ── Step 3: Submit the completed answers ───────────────────────────────────

    /// <inheritdoc/>
    public async Task<FilledForm?> SubmitFormAsync(
        string domain,
        IReadOnlyDictionary<string, string> answers,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafety(domain)) return null;

        // Build the UI form from the gathered answers up front, so the user's input
        // is preserved even if the server round-trip fails.
        var form = BuildFilledForm(answers);

        try
        {
            await WithClientAsync(async (client, tool) =>
            {
                var startResult = await client.CallToolAsync(
                    tool("StartNewIncident"), new Dictionary<string, object?>(),
                    cancellationToken: cancellationToken);

                var incidentId = ExtractIncidentId(startResult);
                if (string.IsNullOrEmpty(incidentId))
                {
                    _logger.LogWarning("[MCP] Could not start incident on submit — UI form still built locally");
                    return false;
                }
                _logger.LogInformation("[MCP] Submitting incident {Id}", incidentId);

                // Submit page by page, using the known safety schema for page grouping
                for (var page = 1; page <= 2; page++)
                {
                    var pageAnswers = new Dictionary<string, string>();
                    foreach (var field in KnownSafetyFields(page))
                        if (answers.TryGetValue(field.Id, out var v) && !string.IsNullOrWhiteSpace(v))
                            pageAnswers[field.Id] = v;

                    var updateResult = await client.CallToolAsync(
                        tool("UpdateIncidentPage"),
                        new Dictionary<string, object?>
                        {
                            ["incidentId"] = incidentId,
                            ["pageNumber"] = page,
                            ["answers"]    = pageAnswers
                        },
                        cancellationToken: cancellationToken);

                    var ok = string.Equals(GetText(updateResult)?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
                    _logger.LogInformation("[MCP] Page {Page} submitted — server accepted: {Ok}", page, ok);
                }

                await client.CallToolAsync(
                    tool("CompleteIncident"),
                    new Dictionary<string, object?> { ["incidentId"] = incidentId },
                    cancellationToken: cancellationToken);
                _logger.LogInformation("[MCP] Incident {Id} completed on server", incidentId);
                return true;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MCP] SubmitFormAsync server call failed — returning locally-built form");
        }

        return form;
    }

    // ── Connection helper ──────────────────────────────────────────────────────

    /// <summary>
    /// Spawns the MCP server, connects over stdio, resolves the registered tool
    /// names (the SDK renames methods to snake_case), runs <paramref name="action"/>,
    /// then disposes the connection. The action receives the client and a tool-name
    /// resolver that maps our PascalCase names to whatever the server registered.
    /// </summary>
    private async Task<T> WithClientAsync<T>(
        Func<McpClient, Func<string, string>, Task<T>> action,
        CancellationToken ct)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name      = "SafetyMcpServer",
            Command   = "dotnet",
            // --no-build: MCPServer is pre-built by Web.csproj's BuildMcpServer target.
            Arguments = ["run", "--project", _serverPath, "--no-build"]
        });

        _logger.LogInformation("[MCP] Spawning Safety MCP server from '{Path}'", _serverPath);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
        _logger.LogInformation("[MCP] Connected to Safety MCP server");

        var registeredTools = await client.ListToolsAsync(cancellationToken: ct);
        var toolNames = registeredTools.ToDictionary(t => Normalise(t.Name), t => t.Name);
        _logger.LogInformation(
            "[MCP] Server registered {Count} tool(s): {Names}",
            toolNames.Count, string.Join(", ", registeredTools.Select(t => t.Name)));

        string Tool(string name) =>
            toolNames.TryGetValue(Normalise(name), out var actual) ? actual : name;

        return await action(client, Tool);
    }

    private static string Normalise(string name) =>
        name.Replace("_", "").Replace("-", "").ToLowerInvariant();

    // ── AI extraction helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Builds a prompt that lists each field with its type and options, then asks
    /// the model to return "FieldName: value" pairs (the proven FORM_FIELDS shape).
    /// </summary>
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

    /// <summary>
    /// Parses "FieldName: value" pairs from the AI's response. Only accepts known
    /// field ids; strips parenthetical notes and placeholder/"unknown" values.
    /// </summary>
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

            // Strip parenthetical explanations: "Slip/Trip/Fall (because...)" → "Slip/Trip/Fall"
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

    // ── Component JSON parsing ─────────────────────────────────────────────────

    /// <summary>
    /// Walks the component JSON from GetPageComponents and extracts field
    /// definitions. Property matching is case-insensitive so it works whether the
    /// MCP SDK serialises camelCase (fieldName) or PascalCase (FieldName).
    /// </summary>
    private static List<McpFormField> ParseFieldsFromComponentJson(string json, int page)
    {
        var fields = new List<McpFormField>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            WalkComponentElement(doc.RootElement, fields, page);
        }
        catch (JsonException)
        {
            // Not valid JSON — caller handles the empty list (falls back to known schema)
        }
        return fields;
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

    /// <summary>Maps the server's component type name to a UI field type.</summary>
    private static string NormaliseType(string serverType, string[]? options) =>
        serverType.ToLowerInvariant() switch
        {
            "radiobuttongroup"          => "radio",
            "datetimepicker"            => "date",
            "textareainput"             => "textarea",
            _ when options is { Length: > 0 } => "radio",
            _                           => "text"
        };

    /// <summary>
    /// Case- and underscore-insensitive property lookup, so "FieldName",
    /// "fieldName", and "field_name" all resolve to the same value.
    /// </summary>
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
    /// Fallback field definitions for the safety form, used when the server's
    /// component JSON can't be parsed. Mirrors the MCPServer's PageDefinitions so
    /// update_incident_page still receives the field names it expects, and the
    /// labels match the server's question text.
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

    // ── FilledForm construction ────────────────────────────────────────────────

    /// <summary>
    /// Maps the collected answers (keyed by MCP field names) into a FilledForm the
    /// UI Forms tab renders. Uses polished display labels; field ids and options
    /// mirror the MCPServer's PageDefinitions exactly.
    /// </summary>
    private static FilledForm BuildFilledForm(IReadOnlyDictionary<string, string> answers)
    {
        static FilledFormField MakeField(
            string id, string label, string type,
            string[]? options, IReadOnlyDictionary<string, string> answers)
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
                        MakeField("PersonsInvolved",       "Persons Involved",        "textarea", null, answers),
                        MakeField("InjuriesReported",      "Injuries Reported",       "textarea", null, answers),
                        MakeField("IncidentDescription",   "Incident Description",    "textarea", null, answers),
                        MakeField("ImmediateActionsTaken", "Immediate Actions Taken", "textarea", null, answers),
                    ]
                }
            ]
        };
    }

    // ── Tool-result helpers ────────────────────────────────────────────────────

    /// <summary>Returns the first text-type content block from a tool result.</summary>
    private static string? GetText(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;

    /// <summary>
    /// Parses the incidentId from a tool result. The server returns the full
    /// SafetyIncident object both as a JSON text block and in StructuredContent,
    /// with camelCase property names — matched case-insensitively.
    /// </summary>
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
}
