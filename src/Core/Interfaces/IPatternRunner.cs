using Core.Models;

namespace Core.Interfaces;

/// <summary>
/// Contract for each orchestration pattern demo.
/// Implementations yield AgentEvents that are streamed to the client via SSE.
/// </summary>
public interface IPatternRunner
{
    /// <summary>Unique pattern identifier matching PatternInfo.Id</summary>
    string PatternId { get; }

    /// <summary>Metadata about this pattern for the UI</summary>
    PatternInfo Info { get; }

    /// <summary>
    /// Executes the pattern against the given prompt, streaming
    /// agent events back to the caller as they occur.
    /// </summary>
    IAsyncEnumerable<AgentEvent> RunAsync(string userPrompt, CancellationToken cancellationToken = default);
}
