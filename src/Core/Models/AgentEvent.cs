using System.Text.Json.Serialization;

namespace Core.Models;

public record AgentEvent(
    string              AgentName,
    string              AgentColour,
    string              Content,
    AgentEventType      EventType,
    DateTimeOffset      Timestamp,
    string?             ConversationId = null,   // ClarificationRequest / FormQuestion
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    StructuredSummaryData? Summary = null,       // StructuredSummary event only
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FilledForm?         Form = null,             // FormFilled event only
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string[]?           Options = null,          // FormQuestion (choice fields) only
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string?             Suggestion = null        // FormQuestion — the AI's suggested choice
)
{
    public static AgentEvent SystemNote(string content) =>
        new("System", "#6b7280", content, AgentEventType.SystemNote, DateTimeOffset.UtcNow);

    public static AgentEvent Thinking(string agentName, string colour) =>
        new(agentName, colour, "Analysing...", AgentEventType.AgentThinking, DateTimeOffset.UtcNow);

    public static AgentEvent Response(string agentName, string colour, string content) =>
        new(agentName, colour, content, AgentEventType.AgentResponse, DateTimeOffset.UtcNow);

    /// <summary>A visible, collapsible explanation of why a decision was made.</summary>
    public static AgentEvent Reasoning(string agentName, string colour, string reasoning) =>
        new(agentName, colour, reasoning, AgentEventType.Reasoning, DateTimeOffset.UtcNow);

    /// <summary>
    /// The agent needs more information from the user before it can proceed.
    /// Carries a ConversationId the client sends back with the user's reply.
    /// </summary>
    public static AgentEvent ClarificationNeeded(
        string agentName, string colour, string question, string conversationId) =>
        new(agentName, colour, question, AgentEventType.ClarificationRequest,
            DateTimeOffset.UtcNow, conversationId);

    /// <summary>
    /// A form-driven question: asks the user to fill one form field.
    /// The question text is the form field's own label. When <paramref name="options"/>
    /// is supplied (choice field), the UI shows them as selectable buttons — and
    /// highlights <paramref name="suggestion"/> as the AI's suggested choice if given;
    /// otherwise it shows a free-text input. The reply comes back via the ConversationId.
    /// </summary>
    public static AgentEvent FormQuestionEvent(
        string agentName, string colour, string question, string conversationId,
        string[]? options, string? suggestion = null) =>
        new(agentName, colour, question, AgentEventType.FormQuestion,
            DateTimeOffset.UtcNow, conversationId, Options: options, Suggestion: suggestion);

    public static AgentEvent Aggregate(string content) =>
        new("Coordinator", "#8b5cf6", content, AgentEventType.AggregatedResult, DateTimeOffset.UtcNow);

    /// <summary>
    /// Emitted by the Coordinator at the end of Phase 3 (synthesis).
    /// Rendered as a colour-coded structured card in the UI.
    /// </summary>
    public static AgentEvent SummaryCard(
        string agentName, string colour, StructuredSummaryData summary) =>
        new(agentName, colour, "", AgentEventType.StructuredSummary,
            DateTimeOffset.UtcNow, Summary: summary);

    /// <summary>
    /// Emitted once per specialist after its FORM_FIELDS block is parsed.
    /// Causes the Forms tab to appear in the UI.
    /// </summary>
    public static AgentEvent FormFilledEvent(
        string agentName, string colour, FilledForm form) =>
        new(agentName, colour, "", AgentEventType.FormFilled,
            DateTimeOffset.UtcNow, Form: form);

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
    Reasoning,              // Decision explanation — shown in a collapsible block
    ClarificationRequest,   // Agent needs more info — stream pauses, user prompted
    FormQuestion,           // Form-driven question for one missing field — stream pauses
    AggregatedResult,       // Non-streaming aggregated result (used by non-hybrid patterns)
    StructuredSummary,      // Coordinator's Phase-3 structured card (Hybrid only)
    FormFilled,             // A specialist's FORM_FIELDS parsed into a FilledForm (Hybrid only)
    Error,
    Complete
}
