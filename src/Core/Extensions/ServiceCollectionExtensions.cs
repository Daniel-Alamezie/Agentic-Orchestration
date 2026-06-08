using Core.Infrastructure;
using Core.Infrastructure.Mcp;
using Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Core.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a shared Kernel instance (Ollama-backed), a ConversationStore, and the
    /// (currently dormant) MCP form client into DI.
    ///
    /// To activate a live MCP server connection swap the IMcpFormClient registration:
    ///   services.AddSingleton&lt;IMcpFormClient, LiveMcpFormClient&gt;();
    /// and set "Mcp:Endpoint" in appsettings (e.g. "http://localhost:3000/sse").
    /// </summary>
    public static IServiceCollection AddOllamaKernel(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Shared LLM connection — all agents use the same kernel.
        services.AddSingleton<Kernel>(_ => KernelFactory.Create(configuration));

        // Holds TaskCompletionSources for conversations paused awaiting user clarification.
        services.AddSingleton<ConversationStore>();

        // MCP form client — live connection to the Safety MCP server (stdio transport).
        // Safety domain: spawns the MCPServer subprocess and uses real tool calls.
        // All other domains: returns null → HybridPatternRunner falls back to text extraction.
        // On any connection failure: returns null → same text extraction fallback.
        // To revert to the no-op dormant client: swap LiveMcpFormClient → DormantMcpFormClient.
        services.AddSingleton<IMcpFormClient, LiveMcpFormClient>();

        return services;
    }
}
