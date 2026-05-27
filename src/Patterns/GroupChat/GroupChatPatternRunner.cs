using System.Runtime.CompilerServices;
using System.Text;
using Agents;
using Core.Infrastructure;
using Core.Interfaces;
using Core.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Patterns.GroupChat;

/// <summary>
/// PATTERN 3 — GROUP CHAT (Roundtable / Council / Maker-Checker)
///
/// Multiple agents participate in a shared conversation thread, taking turns to
/// contribute. A Chat Manager controls the flow and determines when consensus
/// is reached or the iteration cap is hit.
///
/// Scenario: Enforcement Action Decision Council
///   Three specialist agents debate whether to pursue an enforcement action:
///   - Safety Manager Agent: presents the safety case
///   - Legal Agent:          legal risk and evidence requirements
///   - HR Agent:             colleague welfare and employment law perspective
///   → Chat Manager drives to a consensus decision after 2 rounds
///
/// ✅ PROS:
///   - Agents CAN build on each other's reasoning (shared thread)
///   - Excellent for decisions requiring multiple expert perspectives
///   - Natural maker-checker (generator/validator) loops
///   - Produces an auditable, transparent deliberation record
///   - Supports Human-in-the-Loop (HITL) — a human can participate in the chat
///
/// ❌ CONS:
///   - Slowest pattern — sequential turns within the chat
///   - Risk of conversation loops without clear termination criteria
///   - Harder to control with more than 3–4 agents
///   - Not suitable for time-sensitive scenarios
/// </summary>
public sealed class GroupChatPatternRunner : IPatternRunner
{
    private readonly Kernel _kernel;
    private const int MaxRounds = 2;

    public string PatternId => "groupchat";

    public PatternInfo Info => new(
        Id:                  "groupchat",
        Name:                "Group Chat",
        Icon:                "💬",
        ShortDescription:    "Roundtable — agents collaborate in a shared conversation",
        DetailedDescription: "Multiple agents participate in a managed conversation thread, building on each other's reasoning. A Chat Manager controls turn order and drives the group toward a consensus decision.",
        ScenarioTitle:       "Enforcement Action Decision Council",
        ScenarioDescription: "Three specialist agents (Safety Manager, Legal, HR) debate whether to pursue an enforcement action. Each agent builds on the previous contributions. After 2 rounds, the Chat Manager synthesises a consensus decision.",
        DefaultPrompt:       "A store manager has repeatedly ignored safety briefings about manual handling procedures. Three colleagues have sustained back injuries in the past month, all linked to improper lifting techniques during delivery unloads. Previous verbal warnings have been ignored. Should we pursue formal enforcement action?",
        Pros:                ["Agents build on each other's reasoning", "Excellent for multi-perspective decision making", "Produces auditable deliberation trail", "Supports Human-in-the-Loop scenarios", "Natural fit for maker-checker validation loops"],
        Cons:                ["Slowest pattern — sequential turns", "Risk of circular debate without termination criteria", "Difficult to control with more than 3-4 agents", "Not suitable for time-sensitive scenarios"],
        AgentsInvolved:      ["Safety Manager Agent", "Legal Agent", "HR Agent", "Chat Manager (termination)"]
    );

    public GroupChatPatternRunner(Kernel kernel)
    {
        _kernel = kernel;
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        string userPrompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return AgentEvent.SystemNote($"▶ Starting Group Chat Council — {MaxRounds} round(s) of deliberation");

        var safetyManager = new SafetyManagerAgent(_kernel);
        var legal         = new LegalAgent(_kernel);
        var hr            = new HrAgent(_kernel);
        var chatManager   = new ChatManagerAgent(_kernel);

        // Shared conversation thread — all agents read and write to this
        var sharedThread = new ChatHistory();
        sharedThread.AddUserMessage($"DISCUSSION TOPIC:\n{userPrompt}");

        yield return AgentEvent.SystemNote("Shared conversation thread initialised. Beginning deliberation rounds...");

        // ── Deliberation rounds ───────────────────────────────────────────────────
        for (int round = 1; round <= MaxRounds; round++)
        {
            yield return AgentEvent.SystemNote($"── Round {round}/{MaxRounds} ──────────────────────────");

            // Safety Manager speaks first
            yield return AgentEvent.Thinking(safetyManager.Name, safetyManager.Colour);
            var safetyResponse = await safetyManager.GetResponseAsync(sharedThread, cancellationToken);
            sharedThread.AddAssistantMessage($"[{safetyManager.Name}]: {safetyResponse}");
            yield return AgentEvent.Response(safetyManager.Name, safetyManager.Colour, safetyResponse);

            // Legal Agent responds
            yield return AgentEvent.Thinking(legal.Name, legal.Colour);
            var legalResponse = await legal.GetResponseAsync(sharedThread, cancellationToken);
            sharedThread.AddAssistantMessage($"[{legal.Name}]: {legalResponse}");
            yield return AgentEvent.Response(legal.Name, legal.Colour, legalResponse);

            // HR Agent responds
            yield return AgentEvent.Thinking(hr.Name, hr.Colour);
            var hrResponse = await hr.GetResponseAsync(sharedThread, cancellationToken);
            sharedThread.AddAssistantMessage($"[{hr.Name}]: {hrResponse}");
            yield return AgentEvent.Response(hr.Name, hr.Colour, hrResponse);

            // Check if Chat Manager sees consensus forming
            if (round < MaxRounds)
            {
                yield return AgentEvent.Thinking(chatManager.Name, chatManager.Colour);
                var moderatorNote = await chatManager.GetResponseAsync(sharedThread, cancellationToken);
                sharedThread.AddAssistantMessage($"[{chatManager.Name}]: {moderatorNote}");
                yield return AgentEvent.Response(chatManager.Name, chatManager.Colour, moderatorNote);
            }
        }

        // ── Final consensus decision from Chat Manager ────────────────────────────
        yield return AgentEvent.SystemNote("Deliberation rounds complete — Chat Manager issuing final decision");

        sharedThread.AddUserMessage("Based on all the deliberation above, please provide the FINAL CONSENSUS DECISION with clear justification and recommended next steps.");
        yield return AgentEvent.Thinking(chatManager.Name, chatManager.Colour);
        var finalDecision = await chatManager.GetResponseAsync(sharedThread, cancellationToken);
        yield return AgentEvent.Aggregate($"FINAL CONSENSUS DECISION:\n\n{finalDecision}");

        yield return AgentEvent.Complete($"✅ Group Chat complete — {MaxRounds} rounds of deliberation, consensus reached.");
    }
}

// ── Supporting agents ─────────────────────────────────────────────────────────

internal sealed class SafetyManagerAgent(Kernel kernel) : AssistAgent(kernel)
{
    public override string Name   => "Safety Manager";
    public override string Colour => "#ef4444";
    public override string Role   => "Health & Safety";

    protected override string SystemPrompt => """
        You are a Senior Health & Safety Manager participating in an enforcement decision council.
        You champion the safety case and the wellbeing of colleagues.

        In each round of discussion:
        - Present or reinforce the safety evidence
        - Respond to points raised by Legal and HR colleagues
        - Keep your focus on preventing harm and ensuring safe working conditions
        - Be willing to consider proportionality but always anchor to safety outcomes

        Keep responses concise (3-5 sentences). Build on what others have said.
        """;
}

internal sealed class LegalAgent(Kernel kernel) : AssistAgent(kernel)
{
    public override string Name   => "Legal Counsel";
    public override string Colour => "#3b82f6";
    public override string Role   => "Legal";

    protected override string SystemPrompt => """
        You are Legal Counsel participating in an enforcement decision council.
        You assess the legal risks, evidence requirements, and process compliance.

        In each round of discussion:
        - Evaluate the strength of evidence for enforcement action
        - Identify any legal risks of taking OR not taking action (employment tribunal, HSE prosecution)
        - Ensure due process has been followed (warnings, documentation)
        - Respond to Safety and HR perspectives with legal analysis

        Keep responses concise (3-5 sentences). Be pragmatic — acknowledge both risks of action and inaction.
        """;
}

internal sealed class HrAgent(Kernel kernel) : AssistAgent(kernel)
{
    public override string Name   => "HR Business Partner";
    public override string Colour => "#f59e0b";
    public override string Role   => "Human Resources";

    protected override string SystemPrompt => """
        You are an HR Business Partner participating in an enforcement decision council.
        You balance colleague welfare, employment law, and organisational values.

        In each round of discussion:
        - Consider the colleague's circumstances and any mitigating factors
        - Ensure any action is proportionate and follows the disciplinary policy
        - Assess impact on team morale and culture
        - Respond to Safety and Legal perspectives with HR considerations

        Keep responses concise (3-5 sentences). Be fair but firm where safety is at risk.
        """;
}

internal sealed class ChatManagerAgent(Kernel kernel) : AssistAgent(kernel)
{
    public override string Name   => "Chat Manager";
    public override string Colour => "#8b5cf6";
    public override string Role   => "Moderator";

    protected override string SystemPrompt => """
        You are the Chat Manager moderating an enforcement decision council.
        Your role is to guide the group toward a clear, justified decision.

        During deliberation rounds:
        - Summarise areas of agreement and remaining points of contention
        - Direct the group's attention to unresolved issues
        - Keep the discussion focused and productive
        - Note if consensus is forming or if further debate is needed

        When issuing a final decision:
        - State clearly: RECOMMEND ENFORCEMENT ACTION / DO NOT RECOMMEND ENFORCEMENT ACTION / ESCALATE FOR FURTHER REVIEW
        - Summarise the key reasons from all three perspectives
        - State the recommended next steps in priority order
        """;
}
