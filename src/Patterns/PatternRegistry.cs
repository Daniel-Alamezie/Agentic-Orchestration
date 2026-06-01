using Core.Infrastructure;
using Core.Interfaces;
using Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Patterns.Concurrent;
using Patterns.GroupChat;
using Patterns.Handoff;
using Patterns.Hybrid;
using Patterns.Magentic;
using Patterns.Sequential;

namespace Patterns;

/// <summary>
/// Central registry of all pattern runners.
/// Registered as a singleton in DI; each runner lazily creates its agents.
/// </summary>
public sealed class PatternRegistry
{
    private readonly Dictionary<string, IPatternRunner> _runners;

    public PatternRegistry(Kernel kernel, ConversationStore store, IMcpFormClient mcpClient)
    {
        _runners = new Dictionary<string, IPatternRunner>(StringComparer.OrdinalIgnoreCase)
        {
            // ── The five Microsoft patterns ──────────────────────────────────────
            ["sequential"] = new SequentialPatternRunner(kernel),
            ["concurrent"]  = new ConcurrentPatternRunner(kernel),
            ["groupchat"]   = new GroupChatPatternRunner(kernel),
            ["handoff"]     = new HandoffPatternRunner(kernel),
            ["magentic"]    = new MagenticPatternRunner(kernel),
            // ── Recommended approach for the Assist platform ─────────────────────
            ["hybrid"]      = new HybridPatternRunner(kernel, store, mcpClient),
        };
    }

    public IPatternRunner Get(string patternId) =>
        _runners.TryGetValue(patternId, out var runner)
            ? runner
            : throw new KeyNotFoundException($"No pattern registered with id '{patternId}'.");

    public IEnumerable<PatternInfo> GetAllInfo() =>
        _runners.Values.Select(r => r.Info);
}

public static class PatternServiceExtensions
{
    public static IServiceCollection AddPatternRegistry(this IServiceCollection services)
    {
        services.AddSingleton<PatternRegistry>();
        return services;
    }
}
