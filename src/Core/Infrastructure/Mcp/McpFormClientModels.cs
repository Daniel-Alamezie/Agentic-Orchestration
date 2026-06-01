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
