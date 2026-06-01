using System.Runtime.CompilerServices;
using Agents;
using Core.Infrastructure;
using Core.Interfaces;
using Core.Models;
using Microsoft.SemanticKernel;

namespace Patterns.Handoff;

/// <summary>
/// PATTERN 4 — HANDOFF (Routing / Triage / Dynamic Delegation)
///
/// One active agent at a time. Each agent processes the current context and
/// decides whether to handle it or transfer to a more appropriate specialist.
/// The routing decision is made dynamically based on content, not predetermined rules.
///
/// Scenario: Intelligent Incident Triage
///   - Triage Agent:       First contact, classifies the incident type(s)
///   - Safety Specialist:  Handles safety-domain aspects, can hand off to Security
///   - Security Specialist: Handles security-domain aspects, can hand off to Facilities
///   - Facilities Specialist: Handles facilities-domain aspects, closes the chain
///
/// ✅ PROS:
///   - Efficient — only the relevant specialist(s) are invoked
///   - Natural escalation path, similar to real-world triage
///   - Good for scenarios where the right expert isn't known upfront
///   - Each agent can specialise deeply without needing broad knowledge
///
/// ❌ CONS:
///   - Risk of infinite routing loops if not capped
///   - Poor routing decisions degrade user experience significantly
///   - Cannot handle tasks requiring simultaneous specialist input
///   - Chain latency grows with each handoff
/// </summary>
public sealed class HandoffPatternRunner : IPatternRunner
{
    private readonly Kernel _kernel;
    private const int MaxHandoffs = 5;

    public string PatternId => "handoff";

    public PatternInfo Info => new(
        Id:                  "handoff",
        Name:                "Handoff",
        Icon:                "🔀",
        ShortDescription:    "Triage & routing — the right specialist handles each domain",
        DetailedDescription: "A Triage Agent classifies the incident and routes it to the most appropriate specialist. Each specialist handles their domain and decides whether to hand off to another agent or close the chain. Only one agent is active at a time.",
        ScenarioTitle:       "Intelligent Incident Triage",
        ScenarioDescription: "A fault spanning three domains routes through specialists one at a time. Watch how each domain requires a fresh handoff hop — with latency growing at every step. Notice that agents never work in parallel, even when they could.",
        DefaultPrompt:       "A contractor doing electrical work has accidentally cut through a cable in the plant room. The store CCTV system is now partially offline, two emergency exit signs have gone dark, and the fire panel is showing a fault. The contractor is still on site.",
        Pros:                ["Only relevant specialists are invoked — efficient", "Natural escalation path mirroring real-world triage", "Right expert for each domain, deeply specialised", "Good when the required specialist isn't known upfront"],
        Cons:                ["Risk of infinite handoff loops — must cap hops", "Poor routing = significantly degraded experience", "Cannot handle tasks requiring simultaneous experts", "Latency grows linearly with each handoff"],
        AgentsInvolved:      ["Triage Agent", "Safety Specialist (if safety issues)", "Security Specialist (if security issues)", "Facilities Specialist (if facilities issues)"]
    );

    public HandoffPatternRunner(Kernel kernel)
    {
        _kernel = kernel;
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        string userPrompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return AgentEvent.SystemNote("▶ Starting Handoff Triage — routing to the right specialist(s)");

        var triage     = new TriageAgent(_kernel);
        var safety     = new SafetySpecialistAgent(_kernel);
        var security   = new SecuritySpecialistAgent(_kernel);
        var facilities = new FacilitiesSpecialistAgent(_kernel);

        // Map agent names to instances for dynamic routing
        var agentMap = new Dictionary<string, AssistAgent>(StringComparer.OrdinalIgnoreCase)
        {
            ["safety"]     = safety,
            ["security"]   = security,
            ["facilities"] = facilities
        };

        // ── Step 1: Triage classification ─────────────────────────────────────────
        yield return AgentEvent.SystemNote("Triage Agent classifying incident...");
        yield return AgentEvent.Thinking(triage.Name, triage.Colour);

        var triagePrompt = $"""
            Classify this incident and determine which specialist(s) should handle it.

            INCIDENT:
            {userPrompt}

            Respond in this exact format:
            CLASSIFICATION: [brief incident type description]
            PRIMARY_AGENT: [safety | security | facilities]
            REASON: [one sentence why this specialist is most appropriate first]
            """;

        var triageDecision = await triage.GetResponseAsync(triagePrompt, cancellationToken);
        yield return AgentEvent.Response(triage.Name, triage.Colour, triageDecision);

        // Parse the primary agent from triage decision
        var currentAgentKey = ExtractPrimaryAgent(triageDecision);
        if (currentAgentKey is null)
        {
            yield return AgentEvent.Error("Triage agent could not determine a primary specialist. Defaulting to Safety.");
            currentAgentKey = "safety";
        }

        // ── Step 2: Handoff chain ──────────────────────────────────────────────────
        var context = userPrompt;
        var handoffCount = 0;
        var visitedAgents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (currentAgentKey is not null
               && agentMap.TryGetValue(currentAgentKey, out var currentAgent)
               && handoffCount < MaxHandoffs
               && !visitedAgents.Contains(currentAgentKey))
        {
            handoffCount++;
            visitedAgents.Add(currentAgentKey);

            yield return AgentEvent.SystemNote($"Handoff #{handoffCount}: routing to {currentAgent.Name}");
            yield return AgentEvent.Thinking(currentAgent.Name, currentAgent.Colour);

            // Ask agent to handle their domain AND decide if handoff is needed
            var agentPrompt = $"""
                INCIDENT:
                {context}

                Handle the aspects of this incident that fall within your domain.
                After your analysis, determine if another specialist needs to be involved.

                Respond in this format:
                ANALYSIS: [your full domain analysis]
                HANDOFF_TO: [safety | security | facilities | none]
                HANDOFF_REASON: [brief reason, or "n/a" if none]
                """;

            var agentResponse = await currentAgent.GetResponseAsync(agentPrompt, cancellationToken);
            var analysisOnly = ExtractAnalysis(agentResponse);
            yield return AgentEvent.Response(currentAgent.Name, currentAgent.Colour, analysisOnly);

            // Determine next agent
            var nextAgentKey = ExtractHandoffTarget(agentResponse);
            if (nextAgentKey == "none" || nextAgentKey is null || visitedAgents.Contains(nextAgentKey))
            {
                yield return AgentEvent.SystemNote($"{currentAgent.Name} has closed the chain — no further handoff needed.");
                break;
            }

            yield return AgentEvent.SystemNote($"{currentAgent.Name} → handing off to {nextAgentKey.ToUpper()} specialist");
            currentAgentKey = nextAgentKey;
            context = $"Previous context:\n{context}\n\n{currentAgent.Name} findings:\n{analysisOnly}";
        }

        if (handoffCount >= MaxHandoffs)
            yield return AgentEvent.SystemNote($"⚠️ Handoff cap ({MaxHandoffs}) reached — chain terminated to prevent loops.");

        yield return AgentEvent.Complete($"✅ Handoff chain complete — {handoffCount} specialist(s) invoked.");
    }

    /// <summary>
    /// Finds the first line starting with <paramref name="prefix"/> and returns the
    /// first token after the colon, lower-cased. Returns null if not found.
    /// Used by both ExtractPrimaryAgent and ExtractHandoffTarget.
    /// </summary>
    private static string? ExtractLineValue(string text, string prefix)
    {
        foreach (var line in text.Split('\n'))
        {
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0) continue;
            return line[(colonIdx + 1)..].Trim().ToLowerInvariant().Split(' ')[0];
        }
        return null;
    }

    private static string? ExtractPrimaryAgent(string triageDecision)
    {
        var value = ExtractLineValue(triageDecision, "PRIMARY_AGENT:");
        return value is "safety" or "security" or "facilities" ? value : null;
    }

    private static string? ExtractHandoffTarget(string agentResponse) =>
        ExtractLineValue(agentResponse, "HANDOFF_TO:");

    private static string ExtractAnalysis(string agentResponse)
    {
        var analysisLines = new List<string>();
        var inAnalysis    = false;

        foreach (var line in agentResponse.Split('\n'))
        {
            if (line.StartsWith("ANALYSIS:", StringComparison.OrdinalIgnoreCase))
            {
                inAnalysis = true;
                var rest = line["ANALYSIS:".Length..].Trim();
                if (!string.IsNullOrEmpty(rest)) analysisLines.Add(rest);
                continue;
            }
            if (inAnalysis && (line.StartsWith("HANDOFF_TO:",    StringComparison.OrdinalIgnoreCase)
                            || line.StartsWith("HANDOFF_REASON:", StringComparison.OrdinalIgnoreCase)))
                break;
            if (inAnalysis)
                analysisLines.Add(line);
        }

        return analysisLines.Count > 0
            ? string.Join('\n', analysisLines)
            : agentResponse; // fallback: return everything
    }
}

internal sealed class TriageAgent(Kernel kernel) : AssistAgent(kernel)
{
    public override string Name   => "Triage Agent";
    public override string Colour => "#f97316";
    public override string Role   => "Triage & Routing";

    protected override string SystemPrompt => """
        You are an Incident Triage Agent for the Assist platform.
        Your only job is to classify an incident and route it to the correct specialist.

        Available specialists:
        - safety:     Workplace accidents, injuries, hazards, RIDDOR, HSE compliance
        - security:   Access control, CCTV, theft, unauthorised access, threats
        - facilities: Building damage, equipment failure, utilities, fire exits, structural issues

        Analyse the incident and identify the PRIMARY specialist needed first.
        Be decisive — pick the most critical domain as primary.
        """;
}
