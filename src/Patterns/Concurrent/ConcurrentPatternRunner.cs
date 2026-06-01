using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Agents;
using Core.Interfaces;
using Core.Models;
using Microsoft.SemanticKernel;

namespace Patterns.Concurrent;

/// <summary>
/// PATTERN 2 — CONCURRENT (Fan-out / Fan-in / Scatter-Gather)
///
/// All specialist agents receive the SAME input simultaneously and run in parallel.
/// The Coordinator collects all results and synthesises a unified response.
///
/// Scenario: Multi-Domain Incident Analysis
///   Three specialists analyse the same incident concurrently:
///   - Safety Specialist:     Physical safety implications
///   - Security Specialist:   Security and access implications
///   - Facilities Specialist: Building/infrastructure implications
///   → Coordinator synthesises all three into a prioritised action plan
///
/// ✅ PROS:
///   - Fastest pattern for multi-domain analysis
///   - Each specialist brings independent, unbiased perspective
///   - Natural fit for "assess from all angles" use cases
///   - Scales well — adding more specialists doesn't increase total time
///
/// ❌ CONS:
///   - Agents can't build on each other's insights (they run independently)
///   - Requires a good aggregation/synthesis strategy
///   - Resource-intensive — all agents invoke the LLM simultaneously
///   - Contradictory recommendations need conflict resolution
/// </summary>
public sealed class ConcurrentPatternRunner : IPatternRunner
{
    private readonly Kernel _kernel;

    public string PatternId => "concurrent";

    public PatternInfo Info => new(
        Id:                  "concurrent",
        Name:                "Concurrent",
        Icon:                "⚡",
        ShortDescription:    "Fan-out / Fan-in — all agents work in parallel",
        DetailedDescription: "All specialist agents receive the same input simultaneously and work independently. Their results are collected and synthesised by the Coordinator into a unified, prioritised response.",
        ScenarioTitle:       "Multi-Domain Incident Analysis",
        ScenarioDescription: "A clear Facilities-only issue triggers ALL THREE specialists simultaneously. Watch Security produce a response despite having zero domain relevance here — every LLM call fires regardless of whether that agent has anything useful to contribute.",
        DefaultPrompt:       "There is a growing pothole in the staff car park near the main entrance gate. Several colleagues have nearly tripped on it over the past two weeks and it is getting larger. No injuries have occurred yet.",
        Pros:                ["Fastest multi-domain analysis", "Independent specialist perspectives", "Adding more agents doesn't increase latency", "Natural fit for comprehensive incident assessment"],
        Cons:                ["Agents cannot build on each other's insights", "Requires conflict resolution for contradictory advice", "Resource-intensive — all LLM calls fire simultaneously", "Aggregation quality depends on the coordinator"],
        AgentsInvolved:      ["Safety Specialist", "Security Specialist", "Facilities Specialist", "Coordinator (aggregator)"]
    );

    public ConcurrentPatternRunner(Kernel kernel)
    {
        _kernel = kernel;
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        string userPrompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return AgentEvent.SystemNote("▶ Starting Concurrent Analysis — 3 specialists running in parallel");

        // ── Fan-out: all three specialists start simultaneously ───────────────────
        // We use a Channel to safely merge three concurrent async streams.
        var channel = Channel.CreateUnbounded<AgentEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        var safety    = new SafetySpecialistAgent(_kernel);
        var security  = new SecuritySpecialistAgent(_kernel);
        var facilities = new FacilitiesSpecialistAgent(_kernel);

        // Track full text responses for the aggregation step
        var safetyResult    = string.Empty;
        var securityResult  = string.Empty;
        var facilitiesResult = string.Empty;

        // Launch all three agents concurrently, writing their events to the channel.
        // Accumulate the full text from streaming events — avoids a second LLM call per agent.
        var producerTasks = new[]
        {
            Task.Run(async () =>
            {
                var sb = new StringBuilder();
                await foreach (var evt in safety.InvokeStreamingAsync(userPrompt, cancellationToken))
                {
                    if (evt.EventType == AgentEventType.AgentResponse) sb.Append(evt.Content);
                    await channel.Writer.WriteAsync(evt, cancellationToken);
                }
                safetyResult = sb.ToString();
            }, cancellationToken),

            Task.Run(async () =>
            {
                var sb = new StringBuilder();
                await foreach (var evt in security.InvokeStreamingAsync(userPrompt, cancellationToken))
                {
                    if (evt.EventType == AgentEventType.AgentResponse) sb.Append(evt.Content);
                    await channel.Writer.WriteAsync(evt, cancellationToken);
                }
                securityResult = sb.ToString();
            }, cancellationToken),

            Task.Run(async () =>
            {
                var sb = new StringBuilder();
                await foreach (var evt in facilities.InvokeStreamingAsync(userPrompt, cancellationToken))
                {
                    if (evt.EventType == AgentEventType.AgentResponse) sb.Append(evt.Content);
                    await channel.Writer.WriteAsync(evt, cancellationToken);
                }
                facilitiesResult = sb.ToString();
            }, cancellationToken)
        };

        // Complete the channel when all producers are done
        _ = Task.WhenAll(producerTasks).ContinueWith(
            _ => channel.Writer.Complete(),
            CancellationToken.None);

        // ── Fan-in: read and yield all interleaved events ────────────────────────
        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
            yield return evt;

        // Wait for all tasks to complete before aggregating
        await Task.WhenAll(producerTasks);

        // ── Aggregation: Coordinator synthesises all three ───────────────────────
        yield return AgentEvent.SystemNote("Fan-in complete — Coordinator synthesising all specialist reports");

        var coordinator = new CoordinatorAgent(_kernel);
        var aggregatePrompt = $"""
            You have received independent assessments from three specialists for the following incident:

            INCIDENT:
            {userPrompt}

            SAFETY SPECIALIST ASSESSMENT:
            {safetyResult}

            SECURITY SPECIALIST ASSESSMENT:
            {securityResult}

            FACILITIES SPECIALIST ASSESSMENT:
            {facilitiesResult}

            Please synthesise all three assessments into a unified, prioritised incident response plan.
            """;

        await foreach (var evt in coordinator.InvokeStreamingAsync(aggregatePrompt, cancellationToken))
            yield return evt;

        yield return AgentEvent.Complete("✅ Concurrent analysis complete — 3 specialists ran in parallel, coordinator synthesised results.");
    }
}
