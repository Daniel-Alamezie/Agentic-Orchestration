using System.Collections.Concurrent;

namespace Core.Infrastructure;

/// <summary>
/// Holds TaskCompletionSources for conversations that are paused waiting for user input.
///
/// Flow:
///   1. HybridPatternRunner calls Create() to get a conversationId before yielding ClarificationRequest
///   2. Runner then awaits WaitForResponseAsync(id, ct) — the IAsyncEnumerable pauses here;
///      the SSE connection stays open
///   3. Client POSTs to /api/respond with the id and the user's message
///   4. Respond(id, message) completes the TCS → runner resumes and continues streaming
/// </summary>
public sealed class ConversationStore
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();

    /// <summary>Registers a new pending conversation and returns its short ID.</summary>
    public string Create()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _pending[id] = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        return id;
    }

    /// <summary>
    /// Awaits the user's response. Blocks until Respond() is called or the token is cancelled.
    /// </summary>
    public Task<string> WaitForResponseAsync(string conversationId, CancellationToken ct) =>
        _pending.TryGetValue(conversationId, out var tcs)
            ? tcs.Task.WaitAsync(ct)
            : Task.FromException<string>(
                new KeyNotFoundException($"No pending conversation '{conversationId}'."));

    /// <summary>
    /// Delivers the user's reply and unblocks the waiting runner.
    /// Returns false if the conversation ID is not found.
    /// </summary>
    public bool Respond(string conversationId, string message)
    {
        if (!_pending.TryRemove(conversationId, out var tcs)) return false;
        tcs.SetResult(message);
        return true;
    }
}
