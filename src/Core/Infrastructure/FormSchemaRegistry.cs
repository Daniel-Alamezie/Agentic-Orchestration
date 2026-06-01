using Core.Models;

namespace Core.Infrastructure;

/// <summary>
/// Static registry of incident-report form schemas for the three specialist domains.
///
/// Each schema defines pages → fields. The ParseFormFields method extracts the
/// FORM_FIELDS: block a specialist appends to its response and maps the key/value
/// pairs to the schema, producing a FilledForm ready for the UI.
/// </summary>
public static class FormSchemaRegistry
{
    // ── Schema registry ────────────────────────────────────────────────────────
    private static readonly Dictionary<string, FormSchema> Schemas =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["safety"]     = BuildSafetySchema(),
            ["security"]   = BuildSecuritySchema(),
            ["facilities"] = BuildFacilitiesSchema()
        };

    public static string GetTitle(string domain) => domain.ToLowerInvariant() switch
    {
        "safety"     => "Safety Incident Report",
        "security"   => "Security Incident Report",
        "facilities" => "Facilities Maintenance Report",
        _            => $"{domain[..1].ToUpperInvariant()}{domain[1..]} Report"
    };

    // ── Marker variants the LLM may produce ──────────────────────────────────
    // Small models (Llama 3.2 3B) often title-case or add spaces to the marker.
    private static readonly string[] FieldsMarkers =
    [
        "FORM_FIELDS:", "Form Fields:", "FORM FIELDS:",
        "form_fields:", "form fields:", "Form_Fields:"
    ];

    // ── Parse FORM_FIELDS block ────────────────────────────────────────────────

    /// <summary>
    /// Finds the FORM_FIELDS block at the end of a specialist response (accepts any
    /// capitalisation / spacing variant), parses key: value pairs, and maps them to
    /// the schema for <paramref name="domain"/>.
    /// Returns null if no block is found or the domain is unknown.
    /// </summary>
    public static FilledForm? ParseFormFields(string response, string domain)
    {
        var idx = -1;
        var markerLen = 0;
        foreach (var m in FieldsMarkers)
        {
            var i = response.IndexOf(m, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) { idx = i; markerLen = m.Length; break; }
        }
        if (idx < 0) return null;

        if (!Schemas.TryGetValue(domain, out var schema)) return null;

        var block  = response[(idx + markerLen)..].TrimStart('\r', '\n');
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in block.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) break;   // blank line ends the block

            var colon = trimmed.IndexOf(':');
            if (colon <= 0) continue;

            var key = trimmed[..colon].Trim().ToLowerInvariant();
            var val = SanitiseValue(trimmed[(colon + 1)..].Trim());

            // Treat placeholder synonyms as "not filled"
            parsed[key] = IsEmptyValue(val) ? "" : val;
        }

        var pages = schema.Pages.Select((page, pi) => new FilledFormPage
        {
            Number = pi + 1,
            Title  = page.Title,
            Fields = page.FieldDefs.Select(fd =>
            {
                var hasValue = parsed.TryGetValue(fd.Id, out var v) && !string.IsNullOrEmpty(v);
                return new FilledFormField
                {
                    Id       = fd.Id,
                    Label    = fd.Label,
                    Type     = fd.Type,
                    Options  = fd.Options,
                    Value    = hasValue ? v : null,
                    AiFilled = hasValue
                };
            }).ToList()
        }).ToList();

        return new FilledForm
        {
            Domain    = domain.ToLowerInvariant(),
            FormTitle = GetTitle(domain),
            Pages     = pages
        };
    }

    /// <summary>
    /// Removes the FORM_FIELDS block (any capitalisation variant) and everything after it
    /// so the stripped text can be passed to the Coordinator for synthesis.
    /// </summary>
    public static string StripFormFields(string response)
    {
        foreach (var m in FieldsMarkers)
        {
            var idx = response.IndexOf(m, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)  return response[..idx].TrimEnd();
            if (idx == 0) return "";
        }
        return response;
    }

    // ── Internal helpers ───────────────────────────────────────────────────────

    private static string SanitiseValue(string raw)
    {
        // Strip parenthetical explanations: "Medium (Assumed because…)" → "Medium"
        var paren = raw.IndexOf('(');
        if (paren > 0) raw = raw[..paren].Trim();
        // Strip trailing punctuation the model sometimes adds
        return raw.TrimEnd('.', ',', ';');
    }

    private static bool IsEmptyValue(string v) =>
        string.IsNullOrWhiteSpace(v) ||
        v.Equals("unknown",       StringComparison.OrdinalIgnoreCase) ||
        v.Equals("n/a",           StringComparison.OrdinalIgnoreCase) ||
        v.Equals("tbd",           StringComparison.OrdinalIgnoreCase) ||
        v.Equals("skip",          StringComparison.OrdinalIgnoreCase) ||
        v.Equals("not mentioned", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("not known",     StringComparison.OrdinalIgnoreCase) ||
        v.Equals("unspecified",   StringComparison.OrdinalIgnoreCase) ||
        v.Equals("none",          StringComparison.OrdinalIgnoreCase) ||
        v.StartsWith("[",         StringComparison.OrdinalIgnoreCase); // model echoed placeholder

    // ── Schema builders ────────────────────────────────────────────────────────

    private static FormSchema BuildSafetySchema() => new([
        new("Incident Overview",
        [
            new("incident_type", "Incident Type", "select",
                ["Workplace Accident", "Near Miss", "Dangerous Occurrence", "Ill Health", "Other"]),
            new("incident_date", "Incident Date / Time", "text",   null),
            new("location",      "Location",             "text",   null),
            new("description",   "Description",          "textarea", null),
        ]),
        new("Injury & Personnel",
        [
            new("person_role",     "Person Involved",    "select",
                ["Colleague", "Contractor", "Customer", "Visitor", "Unknown"]),
            new("injury_type",     "Type of Injury",     "select",
                ["Fracture", "Laceration", "Burn", "Strain/Sprain", "Contusion", "Crush", "Electrical", "Other", "Unknown"]),
            new("body_part",       "Body Part Affected", "text", null),
            new("first_aid_given", "First Aid Given",    "select",
                ["Yes", "No", "Unknown"]),
        ]),
        new("Regulatory & Closure",
        [
            new("riddor_reportable", "RIDDOR Reportable", "select",
                ["Yes", "No", "Unknown"]),
            new("riddor_category",   "RIDDOR Category",   "select",
                ["Specified Injury", "Over-7-Day Injury", "Dangerous Occurrence", "N/A", "Unknown"]),
            new("area_closed",       "Area Closed",       "select",
                ["Yes", "No", "Unknown"]),
        ])
    ]);

    private static FormSchema BuildSecuritySchema() => new([
        new("Incident Details",
        [
            new("incident_type", "Incident Type", "select",
                ["Break-in", "Theft", "Unauthorised Access", "CCTV Fault", "Threat/Violence", "Suspicious Behaviour"]),
            new("location",    "Location",    "text",    null),
            new("description", "Description", "textarea", null),
        ]),
        new("CCTV & Evidence",
        [
            new("cctv_status",       "CCTV Status",       "select",
                ["Operational", "Offline", "Tampered", "Partially Offline", "Unknown"]),
            new("footage_preserved", "Footage Preserved", "select",
                ["Yes", "No", "Unknown"]),
            new("police_notified",   "Police Notified",   "select",
                ["Yes", "No", "Recommended", "Unknown"]),
        ]),
        new("Threat Assessment",
        [
            new("threat_level",     "Threat Level",     "select",
                ["Low", "Medium", "High", "Critical"]),
            new("area_locked_down", "Area Locked Down", "select",
                ["Yes", "No", "Recommended"]),
        ])
    ]);

    private static FormSchema BuildFacilitiesSchema() => new([
        new("Asset Details",
        [
            new("asset_type",  "Asset Type",  "select",
                ["Electrical", "Structural", "HVAC", "Equipment", "Fire Safety", "Plumbing", "Other"]),
            new("location",    "Location",    "text",    null),
            new("description", "Description", "textarea", null),
        ]),
        new("Damage Assessment",
        [
            new("severity",          "Severity",          "select",
                ["Low", "Medium", "High", "Critical"]),
            new("area_operational",  "Area Operational",  "select",
                ["Yes", "No", "Partially"]),
        ]),
        new("Restoration Plan",
        [
            new("contractor_required",   "Contractor Required",        "select",
                ["Yes", "No", "Unknown"]),
            new("estimated_restoration", "Estimated Restoration Time", "text", null),
        ])
    ]);

    // ── Internal record types ──────────────────────────────────────────────────
    private sealed record FormSchema(List<PageDef> Pages);
    private sealed record PageDef(string Title, List<FieldDef> FieldDefs);
    private sealed record FieldDef(string Id, string Label, string Type, string[]? Options);
}
