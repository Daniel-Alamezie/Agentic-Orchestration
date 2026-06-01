namespace Core.Models;

/// <summary>
/// Parsed structured summary produced by the Coordinator in Phase 3 of the Hybrid pattern.
/// Emitted as an AgentEvent of type StructuredSummary and rendered as a colour-coded card in the UI.
/// </summary>
public sealed class StructuredSummaryData
{
    public string       ExecutiveSummary { get; init; } = "";
    public string       Severity         { get; init; } = "Unknown";
    public List<string> ImmediateActions { get; init; } = [];
    public List<string> Actions24h       { get; init; } = [];
    public string?      RegulatoryNote   { get; init; }
}

/// <summary>
/// A fully or partially pre-filled incident report form for one domain (safety / security / facilities).
/// Built by FormSchemaRegistry from the FORM_FIELDS block a specialist appends to its response.
/// </summary>
public sealed class FilledForm
{
    public string           Domain    { get; init; } = "";
    public string           FormTitle { get; init; } = "";
    public List<FilledFormPage> Pages { get; init; } = [];
}

public sealed class FilledFormPage
{
    public int                 Number { get; init; }
    public string              Title  { get; init; } = "";
    public List<FilledFormField> Fields { get; init; } = [];
}

public sealed class FilledFormField
{
    public string   Id       { get; init; } = "";
    public string   Label    { get; init; } = "";
    /// <summary>text | textarea | select | date</summary>
    public string   Type     { get; init; } = "text";
    public string?  Value    { get; init; }
    /// <summary>True when the AI extracted a value from the incident description.</summary>
    public bool     AiFilled { get; init; }
    /// <summary>Populated for select-type fields.</summary>
    public string[]? Options { get; init; }
}
