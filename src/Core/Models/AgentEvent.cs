namespace Core.Models;

public record AgentEvent(
    string AgentName,
    string AgentColour,
    string Content,
    AgentEventType EventType,
    DateTimeOffset Timestamp
)
{
    public static AgentEvent SystemNote(string content) =>
        new("System", "#6b7280", content, AgentEventType.SystemNote, DateTimeOffset.UtcNow);

    public static AgentEvent Thinking(string agentName, string colour) =>
        new(agentName, colour, "Analysing...", AgentEventType.AgentThinking, DateTimeOffset.UtcNow);

    public static AgentEvent Response(string agentName, string colour, string content) =>
        new(agentName, colour, content, AgentEventType.AgentResponse, DateTimeOffset.UtcNow);

    public static AgentEvent Aggregate(string content) =>
        new("Coordinator", "#8b5cf6", content, AgentEventType.AggregatedResult, DateTimeOffset.UtcNow);

    public static AgentEvent Complete(string summary) =>
        new("System", "#10b981", summary, AgentEventType.Complete, DateTimeOffset.UtcNow);

    public static AgentEvent Error(string message) =>
        new("System", "#ef4444", message, AgentEventType.Error, DateTimeOffset.UtcNow);
}

public enum AgentEventType
{
    SystemNote,
    AgentThinking,
    AgentResponse,
    AggregatedResult,
    Error,
    Complete
}
