namespace Core.Infrastructure.Mcp;

/// <summary>
/// Describes a single tool the MCP server is expected to expose.
/// Used by <see cref="IMcpFormClient.ExpectedTools"/> to act as a living spec
/// for the local MCP server that will be built later.
/// </summary>
public sealed record McpToolDefinition(
    string                      Name,
    string                      Description,
    IReadOnlyList<McpToolParameter> Parameters);

/// <summary>
/// Describes one parameter of an MCP tool — mirrors the JSON Schema
/// "properties" entry that the MCP server will advertise in its tool manifest.
/// </summary>
public sealed record McpToolParameter(
    string   Name,
    string   Type,        // "string" | "number" | "boolean" | "object" | "array"
    string   Description,
    bool     Required,
    string[] Enum);       // non-empty only for enum parameters

/// <summary>
/// A single field on an MCP-served form, discovered from the server's component tree.
/// Drives the conversational form-fill: the runner asks the user about each missing
/// required field, using <see cref="Label"/> as the exact question text.
/// </summary>
public sealed record McpFormField(
    int       PageNumber,
    string    Id,         // the server's field name, e.g. "IncidentLocation"
    string    Label,      // the question text, e.g. "Where did the incident occur?"
    string    Type,       // "text" | "textarea" | "date" | "radio" | "select"
    string[]? Options,    // non-null for choice fields (radio/select)
    bool      Required)
{
    /// <summary>True when this is a choice field — the UI shows selectable options.</summary>
    public bool IsChoice => Options is { Length: > 0 };
}
