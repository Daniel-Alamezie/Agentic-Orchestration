namespace Core.Infrastructure.Mcp;

/// <summary>
/// Static definitions of the three MCP tools the future local server must expose —
/// one per specialist domain (safety / security / facilities).
///
/// These definitions are surfaced via <see cref="IMcpFormClient.ExpectedTools"/> so that
/// anyone building the local MCP server has a single, authoritative spec to implement
/// against. Field names, types, and enum values mirror <see cref="Core.Infrastructure.FormSchemaRegistry"/>.
///
/// Tool naming convention: assist_fill_{domain}_form
/// Input schema: { incident_context: string }  (free-text incident description)
/// Output:       JSON object whose properties match the FORM_FIELDS keys for that domain
/// </summary>
public static class McpToolSchemas
{
    // ── Safety ────────────────────────────────────────────────────────────────

    public static readonly McpToolDefinition SafetyFormTool = new(
        Name:        "assist_fill_safety_form",
        Description: "Analyses a workplace safety incident description and returns pre-filled values " +
                     "for the Assist Safety Incident Report form (RIDDOR-aligned).",
        Parameters:
        [
            new("incident_context", "string",
                "Full natural-language description of the safety incident, " +
                "including location, persons involved, and any injuries reported.",
                Required: true, Enum: []),

            new("incident_type", "string",
                "Classification of the safety incident.",
                Required: false,
                Enum: ["Workplace Accident", "Near Miss", "Dangerous Occurrence", "Ill Health", "Other"]),

            new("incident_date", "string",
                "Date and time of the incident (ISO 8601 preferred).",
                Required: false, Enum: []),

            new("location", "string",
                "Where in the store / site the incident occurred.",
                Required: false, Enum: []),

            new("description", "string",
                "Concise summary of what happened.",
                Required: false, Enum: []),

            new("person_role", "string",
                "Role of the person(s) involved.",
                Required: false,
                Enum: ["Colleague", "Contractor", "Customer", "Visitor", "Unknown"]),

            new("injury_type", "string",
                "Primary type of injury sustained.",
                Required: false,
                Enum: ["Fracture", "Laceration", "Burn", "Strain/Sprain", "Contusion", "Crush", "Electrical", "Other", "Unknown"]),

            new("body_part", "string",
                "Body part(s) affected.",
                Required: false, Enum: []),

            new("first_aid_given", "string",
                "Whether first aid was administered.",
                Required: false,
                Enum: ["Yes", "No", "Unknown"]),

            new("riddor_reportable", "string",
                "Whether the incident meets the RIDDOR reporting threshold.",
                Required: false,
                Enum: ["Yes", "No", "Unknown"]),

            new("riddor_category", "string",
                "RIDDOR reporting category (if applicable).",
                Required: false,
                Enum: ["Specified Injury", "Over-7-Day Injury", "Dangerous Occurrence", "N/A", "Unknown"]),

            new("area_closed", "string",
                "Whether the affected area has been closed pending investigation.",
                Required: false,
                Enum: ["Yes", "No", "Unknown"]),
        ]);

    // ── Security ──────────────────────────────────────────────────────────────

    public static readonly McpToolDefinition SecurityFormTool = new(
        Name:        "assist_fill_security_form",
        Description: "Analyses a workplace security incident description and returns pre-filled values " +
                     "for the Assist Security Incident Report form.",
        Parameters:
        [
            new("incident_context", "string",
                "Full natural-language description of the security incident, " +
                "including what occurred, location, and any suspects or evidence.",
                Required: true, Enum: []),

            new("incident_type", "string",
                "Classification of the security incident.",
                Required: false,
                Enum: ["Break-in", "Theft", "Unauthorised Access", "CCTV Fault", "Threat/Violence", "Suspicious Behaviour"]),

            new("location", "string",
                "Where in the store / site the incident occurred.",
                Required: false, Enum: []),

            new("description", "string",
                "Concise summary of what happened.",
                Required: false, Enum: []),

            new("cctv_status", "string",
                "Operational status of CCTV at the time of the incident.",
                Required: false,
                Enum: ["Operational", "Offline", "Tampered", "Partially Offline", "Unknown"]),

            new("footage_preserved", "string",
                "Whether relevant footage has been preserved for investigation.",
                Required: false,
                Enum: ["Yes", "No", "Unknown"]),

            new("police_notified", "string",
                "Whether the police have been or should be notified.",
                Required: false,
                Enum: ["Yes", "No", "Recommended", "Unknown"]),

            new("threat_level", "string",
                "Assessed threat level.",
                Required: false,
                Enum: ["Low", "Medium", "High", "Critical"]),

            new("area_locked_down", "string",
                "Whether the affected area has been or should be locked down.",
                Required: false,
                Enum: ["Yes", "No", "Recommended"]),
        ]);

    // ── Facilities ────────────────────────────────────────────────────────────

    public static readonly McpToolDefinition FacilitiesFormTool = new(
        Name:        "assist_fill_facilities_form",
        Description: "Analyses a facilities / maintenance incident description and returns pre-filled " +
                     "values for the Assist Facilities Maintenance Report form.",
        Parameters:
        [
            new("incident_context", "string",
                "Full natural-language description of the facilities issue, " +
                "including asset affected, location, and extent of damage.",
                Required: true, Enum: []),

            new("asset_type", "string",
                "Category of the asset or system affected.",
                Required: false,
                Enum: ["Electrical", "Structural", "HVAC", "Equipment", "Fire Safety", "Plumbing", "Other"]),

            new("location", "string",
                "Where in the store / site the asset is located.",
                Required: false, Enum: []),

            new("description", "string",
                "Concise description of the fault or damage.",
                Required: false, Enum: []),

            new("severity", "string",
                "Severity of the asset failure or damage.",
                Required: false,
                Enum: ["Low", "Medium", "High", "Critical"]),

            new("area_operational", "string",
                "Whether the affected area can continue to operate.",
                Required: false,
                Enum: ["Yes", "No", "Partially"]),

            new("contractor_required", "string",
                "Whether an external contractor is needed for remediation.",
                Required: false,
                Enum: ["Yes", "No", "Unknown"]),

            new("estimated_restoration", "string",
                "Estimated time to restore normal operation (free text, e.g. '4 hours', '2 days').",
                Required: false, Enum: []),
        ]);

    // ── Convenience collection ────────────────────────────────────────────────

    /// <summary>All three tool definitions indexed by domain key (lower-case).</summary>
    public static IReadOnlyDictionary<string, McpToolDefinition> ByDomain { get; } =
        new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["safety"]     = SafetyFormTool,
            ["security"]   = SecurityFormTool,
            ["facilities"] = FacilitiesFormTool,
        };

    /// <summary>Ordered list of all tool definitions (safety → security → facilities).</summary>
    public static IReadOnlyList<McpToolDefinition> All { get; } =
    [
        SafetyFormTool,
        SecurityFormTool,
        FacilitiesFormTool,
    ];
}
