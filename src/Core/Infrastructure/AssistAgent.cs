using System.Runtime.CompilerServices;
using System.Text;
using Core.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Core.Infrastructure;

/// <summary>
/// Base class for all specialist agents in the POC.
/// Wraps Semantic Kernel's IChatCompletionService with a consistent
/// streaming interface that emits AgentEvents for the UI.
/// </summary>
public abstract class AssistAgent
{
    protected readonly Kernel Kernel;
    private readonly IChatCompletionService _chat;

    public abstract string Name { get; }

    /// <summary>Hex colour for this agent's messages in the UI</summary>
    public abstract string Colour { get; }

    /// <summary>Short role label shown in the UI beside the agent name</summary>
    public abstract string Role { get; }

    protected abstract string SystemPrompt { get; }

    protected AssistAgent(Kernel kernel)
    {
        Kernel = kernel;
        _chat = kernel.GetRequiredService<IChatCompletionService>();
    }

    /// <summary>
    /// Streams the agent's response as AgentEvents.
    /// First emits a "thinking" event, then token-by-token response chunks.
    /// </summary>
    public async IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        string input,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return AgentEvent.Thinking(Name, Colour);

        var history = BuildHistory(input);
        var sb = new StringBuilder();

        await foreach (var chunk in _chat.GetStreamingChatMessageContentsAsync(history, cancellationToken: ct))
        {
            if (string.IsNullOrEmpty(chunk.Content)) continue;
            sb.Append(chunk.Content);
            yield return AgentEvent.Response(Name, Colour, chunk.Content);
        }
    }

    /// <summary>
    /// Returns the full response as a single string (for use inside multi-agent flows
    /// where you need the complete output before passing it on).
    /// </summary>
    public async Task<string> GetResponseAsync(string input, CancellationToken ct = default)
    {
        var history = BuildHistory(input);
        var result = await _chat.GetChatMessageContentAsync(history, cancellationToken: ct);
        return result.Content ?? string.Empty;
    }

    /// <summary>
    /// Responds within an existing conversation history (Group Chat / Handoff patterns).
    /// The system prompt is prepended automatically.
    /// </summary>
    public async Task<string> GetResponseAsync(ChatHistory existingHistory, CancellationToken ct = default)
    {
        var history = new ChatHistory();
        history.AddSystemMessage(SystemPrompt);
        foreach (var msg in existingHistory)
            history.Add(msg);

        var result = await _chat.GetChatMessageContentAsync(history, cancellationToken: ct);
        return result.Content ?? string.Empty;
    }

    private ChatHistory BuildHistory(string input)
    {
        var history = new ChatHistory();
        history.AddSystemMessage(SystemPrompt);
        history.AddUserMessage(input);
        return history;
    }
}
