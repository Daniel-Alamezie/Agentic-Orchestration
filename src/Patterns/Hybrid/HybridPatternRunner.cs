using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Agents;
using Core.Infrastructure;
using Core.Interfaces;
using Core.Models;
using Microsoft.SemanticKernel;

namespace Patterns.Hybrid;

/// <summary>
/// PATTERN 6 — RECOMMENDED HYBRID (Classify → Selective Concurrent Fan-out → Synthesise)
///
/// This is the recommended production pattern for a multi-agent architecture.
/// It combines three patterns in sequence, using each where it's most appropriate:
///
///   Phase 1  [Handoff-inspired]   A Gateway Classifier decides WHICH specialists are
///                                 needed and crafts a TAILORED question for each.
///
///   Phase 2  [Concurrent]         Only the relevant specialist(s) run in parallel.
///                                 A pure safety query invokes only the Safety agent;
///                                 a multi-domain query fans out to 2–3 agents.
///
///   Phase 3  [Sequential]         The Coordinator synthesises all findings into a
///                                 single, prioritised response for the user.
///
/// WHY THIS BEATS RUNNING ALL THREE AGENTS EVERY TIME:
///   - Efficiency:     A "wet floor" query doesn't need Security or Facilities.
///                     We save 2 unnecessary LLM calls.
///   - Focus:          Each specialist receives a question tailored to their domain,
///                     not a raw prompt that makes them guess what they're answering.
///   - Speed:          Fewer agents = faster total time for simple queries;
///                     complex queries still benefit from full concurrency.
///   - Transparency:   The classification step is visible — the user can see WHY
///                     each specialist was or wasn't involved.
///
/// ✅ PROS:
///   - Intelligent routing — only relevant specialists are invoked
///   - Each specialist receives a focused, domain-specific question
///   - Scales: 1 domain = 1 agent; 3 domains = 3 parallel agents
///   - Maps directly to the Gateway pattern in a multi-agent architecture
///   - Good balance of speed, cost, and response quality
///
/// ❌ CONS:
///   - Classifier adds one extra LLM call at the start
///   - If the classifier misclassifies, a relevant specialist is missed
///   - Slightly more complex than running all agents blindly every time
///   - Tailored questions require the classifier to be prompt-engineered well
/// </summary>
public sealed class HybridPatternRunner : IPatternRunner
{
    private readonly Kernel _kernel;

    public string PatternId => "hybrid";

    public PatternInfo Info => new(
        Id:                  "hybrid",
        Name:                "Hybrid (Recommended)",
        Icon:                "🎯",
        ShortDescription:    "Classify → selective concurrent fan-out → synthesise",
        DetailedDescription: "The recommended production pattern. A Classifier Agent first determines which specialists are genuinely needed and crafts a tailored question for each. Only relevant agents run — in parallel. The Coordinator synthesises results. This combines the intelligence of Handoff, the speed of Concurrent, and the clarity of Sequential into a single cohesive flow.",
        ScenarioTitle:       "Intelligent Gateway — Multi-Domain Incident",
        ScenarioDescription: "The Gateway Classifier analyses the prompt, selects only the relevant specialists (1–3), tailors a focused question for each, fans them out in parallel, and synthesises a unified response. Try a pure safety query, a security+facilities query, or a complex multi-domain incident to see the classifier dynamically adjust.",
        DefaultPrompt:       "A forklift truck has clipped a racking bay in the warehouse, causing three pallets of stock to fall. One colleague has a suspected broken arm. The racking unit looks structurally unstable and may collapse further. The CCTV covering that zone has been offline for two days.",
        Pros:                ["Only relevant specialists invoked — efficient & focused", "Tailored domain questions = higher quality responses", "Scales naturally: 1 to 3 parallel agents as needed", "Maps directly to the Gateway pattern in any multi-agent system", "Classification step visible in UI — fully transparent"],
        Cons:                ["One extra LLM call for classification upfront", "Classifier errors can cause a relevant specialist to be missed", "Prompt engineering the classifier well is critical", "Slightly more complex than naive 'run all agents'"],
        AgentsInvolved:      ["Gateway Classifier Agent", "Safety Specialist (if needed)", "Security Specialist (if needed)", "Facilities Specialist (if needed)", "Coordinator (synthesis)"]
    );

    public HybridPatternRunner(Kernel kernel)
    {
        _kernel = kernel;
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        string userPrompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return AgentEvent.SystemNote("▶ Starting Hybrid Pattern — Gateway classifying prompt...");

        var classifier = new GatewayClassifierAgent(_kernel);
        var safety     = new SafetySpecialistAgent(_kernel);
        var security   = new SecuritySpecialistAgent(_kernel);
        var facilities = new FacilitiesSpecialistAgent(_kernel);
        var coordinator = new CoordinatorAgent(_kernel);

        var allAgents = new Dictionary<string, AssistAgent>(StringComparer.OrdinalIgnoreCase)
        {
            ["safety"]     = safety,
            ["security"]   = security,
            ["facilities"] = facilities
        };

        // ── Phase 1: Gateway Classifier ──────────────────────────────────────────
        yield return AgentEvent.Thinking(classifier.Name, classifier.Colour);

        var classificationPrompt = $"""
            Analyse this workplace prompt and determine which specialist(s) should handle it.

            PROMPT:
            {userPrompt}

            Available specialists:
            - safety:     Injuries, accidents, near-misses, hazards, RIDDOR, HSE compliance, PPE
            - security:   Unauthorised access, CCTV, theft, threats, access control breaches
            - facilities: Structural damage, racking, equipment, utilities, building integrity, plant

            For EACH specialist that is relevant, write a tailored question they should answer.
            If a specialist is NOT relevant to this prompt, do not include them.

            Respond in EXACTLY this format (include only relevant specialists):
            CLASSIFICATION_REASONING: [1-2 sentences explaining your routing decision]
            SELECTED_AGENTS: [comma-separated list from: safety, security, facilities]
            SAFETY_QUESTION: [specific question for safety specialist, or SKIP]
            SECURITY_QUESTION: [specific question for security specialist, or SKIP]
            FACILITIES_QUESTION: [specific question for facilities specialist, or SKIP]
            """;

        var classificationResponse = await classifier.GetResponseAsync(classificationPrompt, cancellationToken);
        yield return AgentEvent.Response(classifier.Name, classifier.Colour, classificationResponse);

        // ── Parse classification ──────────────────────────────────────────────────
        var selected = ParseSelectedAgents(classificationResponse);
        var tailoredQuestions = ParseTailoredQuestions(classificationResponse, userPrompt);

        if (selected.Count == 0)
        {
            yield return AgentEvent.SystemNote("⚠️ Classifier could not identify relevant specialists. Defaulting to Safety.");
            selected.Add("safety");
            tailoredQuestions["safety"] = userPrompt;
        }

        var agentNames = string.Join(", ", selected.Select(s => s.ToUpperInvariant()));
        yield return AgentEvent.SystemNote(
            $"Classifier selected {selected.Count} specialist(s): {agentNames}. " +
            $"{3 - selected.Count} specialist(s) not needed — skipped.");

        // ── Phase 2: Selective Concurrent Fan-out ────────────────────────────────
        if (selected.Count == 1)
        {
            yield return AgentEvent.SystemNote($"Single specialist needed — no fan-out overhead.");
        }
        else
        {
            yield return AgentEvent.SystemNote($"{selected.Count} specialists running concurrently...");
        }

        var channel = Channel.CreateUnbounded<AgentEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        // Collect full text results for synthesis
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resultLock = new object();

        // Launch only selected agents — each gets their tailored question
        var producerTasks = selected
            .Where(key => allAgents.ContainsKey(key))
            .Select(key => Task.Run(async () =>
            {
                var agent = allAgents[key];
                var question = tailoredQuestions.TryGetValue(key, out var q) ? q : userPrompt;

                // Get full response for synthesis
                var fullResponse = await agent.GetResponseAsync(question, cancellationToken);
                lock (resultLock) { results[key] = fullResponse; }

                // Stream events to the channel
                await foreach (var evt in agent.InvokeStreamingAsync(question, cancellationToken))
                    await channel.Writer.WriteAsync(evt, cancellationToken);

            }, cancellationToken))
            .ToArray();

        _ = Task.WhenAll(producerTasks).ContinueWith(
            _ => channel.Writer.Complete(),
            CancellationToken.None);

        // Stream all interleaved agent events to the caller
        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
            yield return evt;

        await Task.WhenAll(producerTasks);

        // ── Phase 3: Coordinator Synthesis ───────────────────────────────────────
        yield return AgentEvent.SystemNote("All selected specialists complete — Coordinator synthesising...");

        var synthesisPrompt = BuildSynthesisPrompt(userPrompt, results);
        await foreach (var evt in coordinator.InvokeStreamingAsync(synthesisPrompt, cancellationToken))
            yield return evt;

        var skippedCount = 3 - selected.Count;
        var skippedNote = skippedCount > 0
            ? $" ({skippedCount} specialist(s) correctly skipped as not relevant)"
            : "";

        yield return AgentEvent.Complete(
            $"✅ Hybrid pattern complete — {selected.Count} specialist(s) invoked{skippedNote}, " +
            $"classified → concurrent → synthesised.");
    }

    // ── Parsing helpers ───────────────────────────────────────────────────────

    private static List<string> ParseSelectedAgents(string response)
    {
        var result = new List<string>();
        foreach (var line in response.Split('\n'))
        {
            if (!line.StartsWith("SELECTED_AGENTS:", StringComparison.OrdinalIgnoreCase)) continue;
            var value = line.Substring("SELECTED_AGENTS:".Length).Trim();
            foreach (var part in value.Split(','))
            {
                var key = part.Trim().ToLowerInvariant().TrimEnd('.');
                if (key is "safety" or "security" or "facilities")
                    result.Add(key);
            }
            break;
        }
        return result;
    }

    private static Dictionary<string, string> ParseTailoredQuestions(
        string response, string fallbackPrompt)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lineMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SAFETY_QUESTION"]     = "safety",
            ["SECURITY_QUESTION"]   = "security",
            ["FACILITIES_QUESTION"] = "facilities"
        };

        foreach (var line in response.Split('\n'))
        {
            foreach (var (prefix, domain) in lineMap)
            {
                if (!line.StartsWith($"{prefix}:", StringComparison.OrdinalIgnoreCase)) continue;
                var question = line.Substring(prefix.Length + 1).Trim();
                if (!string.IsNullOrWhiteSpace(question) &&
                    !question.Equals("SKIP", StringComparison.OrdinalIgnoreCase))
                {
                    map[domain] = question;
                }
            }
        }

        return map;
    }

    private static string BuildSynthesisPrompt(
        string originalPrompt,
        Dictionary<string, string> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Synthesise the following specialist assessments into a unified incident response.");
        sb.AppendLine();
        sb.AppendLine($"ORIGINAL INCIDENT:");
        sb.AppendLine(originalPrompt);
        sb.AppendLine();

        foreach (var (domain, finding) in results)
        {
            sb.AppendLine($"{domain.ToUpperInvariant()} SPECIALIST ASSESSMENT:");
            sb.AppendLine(finding);
            sb.AppendLine();
        }

        if (results.Count < 3)
        {
            var skipped = new[] { "safety", "security", "facilities" }
                .Except(results.Keys, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (skipped.Count > 0)
            {
                sb.AppendLine($"NOTE: {string.Join(", ", skipped).ToUpperInvariant()} specialist(s) were not invoked " +
                              $"as this incident does not involve their domain.");
            }
        }

        return sb.ToString();
    }
}

// ── Gateway Classifier Agent ──────────────────────────────────────────────────

internal sealed class GatewayClassifierAgent(Kernel kernel) : AssistAgent(kernel)
{
    public override string Name   => "Gateway Classifier";
    public override string Colour => "#f59e0b"; // amber — the gateway/router
    public override string Role   => "Gateway";

    protected override string SystemPrompt => """
        You are the Gateway Classifier for a multi-domain workplace
        health, safety, and security system for a large UK retail organisation.

        Your ONLY job is to read a workplace prompt and decide:
        1. Which specialist domains are genuinely relevant (safety / security / facilities)
        2. What specific, focused question to ask each relevant specialist

        Be precise and selective:
        - DO include a specialist if their domain is clearly relevant to the prompt
        - DO NOT include a specialist just "in case" — if the prompt has no security
          relevance, do not include the Security Specialist
        - Tailor each specialist's question to the specific aspect of the prompt
          relevant to their domain — do not just repeat the full prompt

        Think of yourself as the intelligent gateway deciding which experts to wake up
        and exactly what to ask them. Irrelevant experts should remain idle.
        """;
}
