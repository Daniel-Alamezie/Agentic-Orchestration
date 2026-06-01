using Core.Infrastructure.Mcp;
using Core.Models;

namespace Core.Interfaces;

/// <summary>
/// Abstraction over an MCP (Model Context Protocol) server that exposes
/// form-filling tools for the Assist platform's three specialist domains.
///
/// Current implementations:
///   DormantMcpFormClient — registered by default; returns null so the caller
///                          falls back to structured text extraction.
///
/// Future implementation (when local MCP server is available):
///   LiveMcpFormClient — connects to the configured MCP endpoint, calls the
///                       real tool, and returns a fully populated FilledForm.
///
/// To activate the live client:
///   1. Implement LiveMcpFormClient (using SK's MCP client support or the
///      ModelContextProtocol.Client NuGet package).
///   2. Swap the DI registration in ServiceCollectionExtensions:
///      services.AddSingleton&lt;IMcpFormClient, LiveMcpFormClient&gt;();
///   3. Set "Mcp:Endpoint" in appsettings (e.g. "http://localhost:3000/sse").
/// </summary>
public interface IMcpFormClient
{
    /// <summary>
    /// True when the client is actively connected to a live MCP server.
    /// The HybridPatternRunner uses this to decide whether to attempt a tool call.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Describes the tools this client expects the MCP server to expose.
    /// Use this as the spec when building the local MCP server.
    /// </summary>
    IReadOnlyList<McpToolDefinition> ExpectedTools { get; }

    /// <summary>
    /// Attempts to fill a form for the given specialist domain by invoking the
    /// appropriate MCP tool with the incident context.
    ///
    /// Returns null when:
    ///   - The client is dormant (not connected)
    ///   - The tool call fails or times out
    ///   - The domain has no registered tool
    ///
    /// The caller should fall back to FormSchemaRegistry.ParseFormFields when null
    /// is returned.
    /// </summary>
    Task<FilledForm?> TryFillFormAsync(
        string domain,
        string incidentContext,
        CancellationToken cancellationToken = default);
}
