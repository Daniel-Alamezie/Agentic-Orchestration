using Core.Interfaces;
using Core.Models;
using Microsoft.Extensions.Logging;

namespace Core.Infrastructure.Mcp;

/// <summary>
/// Dormant (no-op) implementation of <see cref="IMcpFormClient"/>.
///
/// Registered by default until a local MCP server is available.
/// Every call returns <c>null</c>, causing the caller to fall back to
/// structured text extraction via <see cref="FormSchemaRegistry.ParseFormFields"/>.
///
/// To activate a live connection:
///   1. Implement <c>LiveMcpFormClient</c> using the ModelContextProtocol.Client
///      NuGet package (or Semantic Kernel's MCP support).
///   2. In <c>ServiceCollectionExtensions</c>, swap:
///         services.AddSingleton&lt;IMcpFormClient, DormantMcpFormClient&gt;();
///      for:
///         services.AddSingleton&lt;IMcpFormClient, LiveMcpFormClient&gt;();
///   3. Set <c>Mcp:Endpoint</c> in appsettings (e.g. "http://localhost:3000/sse").
///
/// The three tools this client expects are defined in <see cref="McpToolSchemas"/>;
/// use those definitions as the contract when building the local MCP server.
/// </summary>
public sealed class DormantMcpFormClient(ILogger<DormantMcpFormClient> logger) : IMcpFormClient
{
    /// <inheritdoc/>
    /// Always false — no server is connected.
    public bool IsConnected => false;

    /// <inheritdoc/>
    /// Returns the tool specs the future live server must expose.
    public IReadOnlyList<McpToolDefinition> ExpectedTools => McpToolSchemas.All;

    /// <inheritdoc/>
    /// Always returns <c>null</c>; caller should fall back to text extraction.
    public Task<FilledForm?> TryFillFormAsync(
        string domain,
        string incidentContext,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "[MCP] DormantMcpFormClient — skipping tool call for domain '{Domain}'. " +
            "No MCP server connected; falling back to structured text extraction.",
            domain);

        return Task.FromResult<FilledForm?>(null);
    }
}
