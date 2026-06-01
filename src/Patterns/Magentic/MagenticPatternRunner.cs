using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Agents;
using Core.Infrastructure;
using Core.Interfaces;
using Core.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Patterns.Magentic;

/// <summary>
/// PATTERN 5 — MAGENTIC (Dynamic / Adaptive Planning / Task-Ledger-Based)
///
/// A Manager Agent dynamically builds a task ledger — a plan of approach — by
/// consulting specialist agents. The ledger evolves as new information arrives.
/// The Manager assigns and reorders tasks as understanding of the problem deepens.
///
/// This is the most powerful but also most expensive pattern.
///
/// Scenario: Complex Multi-Domain Incident Response
///   A serious incident spans Safety, Security, and Facilities.
///   The Manager Agent doesn't know upfront what steps are needed.
///   It builds a task ledger, assigns tasks to specialists, reviews findings,
///   and adapts the plan before issuing the final response.
///
/// ✅ PROS:
///   - Best for open-ended, complex problems with no predetermined solution path
///   - Manager can adapt the plan as information arrives (backtrack, add tasks)
///   - Produces a documented, auditable plan that humans can review before execution
///   - Most closely mirrors how a real incident commander would operate
///
/// ❌ CONS:
///   - Slowest and most expensive pattern (multiple LLM calls per task)
///   - Can stall or loop on ambiguous goals without clear termination criteria
///   - Complexity of the planning layer makes it harder to debug
///   - Overkill for simple, well-defined tasks
/// </summary>
public sealed class MagenticPatternRunner : IPatternRunner
{
    private readonly Kernel _kernel;
    private const int MaxIterations = 6;

    public string PatternId => "magentic";

    public PatternInfo Info => new(
        Id:                  "magentic",
        Name:                "Magentic",
        Icon:                "🧠",
        ShortDescription:    "Dynamic planning — manager builds & adapts a task ledger",
        DetailedDescription: "A Manager Agent dynamically creates a task ledger, consulting specialists as needed. The plan evolves as information arrives — tasks can be added, removed, or reordered. The manager checks progress against the original goal before declaring completion.",
        ScenarioTitle:       "Complex Multi-Domain Incident Response",
        ScenarioDescription: "A routine maintenance observation triggers the full Magentic planning cycle. Watch the Manager build a dynamic task ledger for a clearly-scoped, single-domain issue — powerful when scope is genuinely unknown, but costly overhead when it isn't.",
        DefaultPrompt:       "A ceiling tile in the stockroom appears to have water damage and is sagging slightly. There is no active leak visible but the tile looks like it could fall at some point. No injuries or immediate risk. A colleague noticed it during a routine walk-around.",
        Pros:                ["Best for open-ended, complex incidents", "Plan adapts dynamically as information arrives", "Produces auditable task ledger — great for post-incident review", "Most closely mirrors real incident command structures", "Manager can backtrack and add tasks based on new findings"],
        Cons:                ["Most expensive — multiple LLM calls per task", "Can stall without clear termination criteria", "Planning overhead adds significant latency", "Overkill for well-defined, predictable tasks", "Harder to debug than other patterns"],
        AgentsInvolved:      ["Manager Agent (planner)", "Safety Specialist", "Security Specialist", "Facilities Specialist", "Coordinator (final synthesis)"]
    );

    public MagenticPatternRunner(Kernel kernel)
    {
        _kernel = kernel;
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        string userPrompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return AgentEvent.SystemNote("▶ Starting Magentic Orchestration — Manager building task ledger");

        var manager    = new ManagerAgent(_kernel);
        var safety     = new SafetySpecialistAgent(_kernel);
        var security   = new SecuritySpecialistAgent(_kernel);
        var facilities = new FacilitiesSpecialistAgent(_kernel);
        var coordinator = new CoordinatorAgent(_kernel);

        var agentMap = new Dictionary<string, AssistAgent>(StringComparer.OrdinalIgnoreCase)
        {
            ["safety"]     = safety,
            ["security"]   = security,
            ["facilities"] = facilities
        };

        // ── Phase 1: Manager builds initial task ledger ───────────────────────────
        yield return AgentEvent.SystemNote("Phase 1: Manager Agent analysing incident and building task ledger...");
        yield return AgentEvent.Thinking(manager.Name, manager.Colour);

        var ledgerPrompt = $"""
            You are managing the response to a serious incident. Analyse the situation
            and create an initial task ledger — a prioritised list of investigation tasks.

            INCIDENT:
            {userPrompt}

            Create a task ledger with 3-5 tasks. For each task specify:
            - Which specialist should handle it: safety, security, or facilities
            - What specific question or investigation they should perform
            - Why this task is needed

            Format your response as:
            LEDGER:
            TASK_1: [agent: safety|security|facilities] [question/task description]
            TASK_2: [agent: safety|security|facilities] [question/task description]
            ... etc

            REASONING: [brief explanation of your approach]
            """;

        var ledgerResponse = await manager.GetResponseAsync(ledgerPrompt, cancellationToken);
        yield return AgentEvent.Response(manager.Name, manager.Colour, ledgerResponse);

        // Parse tasks from ledger
        var tasks = ParseTaskLedger(ledgerResponse);
        yield return AgentEvent.SystemNote($"Task ledger created with {tasks.Count} initial tasks");

        // ── Phase 2: Execute tasks, collect findings ──────────────────────────────
        var findings = new StringBuilder();
        var completedTasks = new List<string>();
        var iteration = 0;

        foreach (var task in tasks)
        {
            if (cancellationToken.IsCancellationRequested || iteration >= MaxIterations) break;
            iteration++;

            if (!agentMap.TryGetValue(task.Agent, out var agent))
            {
                yield return AgentEvent.SystemNote($"⚠️ Unknown agent '{task.Agent}' for task: {task.Description}. Skipping.");
                continue;
            }

            yield return AgentEvent.SystemNote($"Executing Task {iteration}: [{agent.Name}] {task.Description}");
            yield return AgentEvent.Thinking(agent.Name, agent.Colour);

            var taskInput = $"""
                INCIDENT CONTEXT:
                {userPrompt}

                YOUR ASSIGNED TASK:
                {task.Description}

                Focus specifically on this task. Be thorough but concise.
                """;

            var taskResult = await agent.GetResponseAsync(taskInput, cancellationToken);
            yield return AgentEvent.Response(agent.Name, agent.Colour, taskResult);

            findings.AppendLine($"[{agent.Name}] Task: {task.Description}");
            findings.AppendLine($"Findings: {taskResult}");
            findings.AppendLine();
            completedTasks.Add($"{agent.Name}: {task.Description}");
        }

        // ── Phase 3: Manager reviews findings and adapts ledger ───────────────────
        yield return AgentEvent.SystemNote("Phase 3: Manager reviewing findings and checking if plan needs adaptation...");
        yield return AgentEvent.Thinking(manager.Name, manager.Colour);

        var reviewPrompt = $"""
            You are reviewing the findings from your initial task ledger execution.

            ORIGINAL INCIDENT:
            {userPrompt}

            COMPLETED TASKS AND FINDINGS:
            {findings}

            Review the findings and determine:
            1. Are there any critical gaps or new issues uncovered that require additional investigation?
            2. Has the original goal been sufficiently addressed?
            3. What is the overall status?

            Respond with:
            STATUS: [IN_PROGRESS | SUFFICIENT]
            NEW_TASKS: [list any additional tasks needed, or "none"]
            GAPS_IDENTIFIED: [brief description of any gaps found]
            """;

        var reviewResponse = await manager.GetResponseAsync(reviewPrompt, cancellationToken);
        yield return AgentEvent.Response(manager.Name, manager.Colour, reviewResponse);

        // Execute any additional tasks the manager identified
        if (!reviewResponse.Contains("NEW_TASKS: none", StringComparison.OrdinalIgnoreCase)
            && !reviewResponse.Contains("NEW_TASKS:\nnone", StringComparison.OrdinalIgnoreCase)
            && iteration < MaxIterations)
        {
            var additionalTasks = ParseTaskLedger(reviewResponse);
            foreach (var task in additionalTasks.Take(2)) // cap at 2 additional tasks
            {
                if (cancellationToken.IsCancellationRequested || iteration >= MaxIterations) break;
                iteration++;

                if (!agentMap.TryGetValue(task.Agent, out var agent)) continue;

                yield return AgentEvent.SystemNote($"Additional Task {iteration}: [{agent.Name}] {task.Description}");
                yield return AgentEvent.Thinking(agent.Name, agent.Colour);

                var addlInput = $"CONTEXT:\n{userPrompt}\n\nFINDINGS SO FAR:\n{findings}\n\nADDITIONAL TASK:\n{task.Description}";
                var addlResult = await agent.GetResponseAsync(addlInput, cancellationToken);
                yield return AgentEvent.Response(agent.Name, agent.Colour, addlResult);

                findings.AppendLine($"[{agent.Name}] Additional Task: {task.Description}");
                findings.AppendLine($"Findings: {addlResult}");
                findings.AppendLine();
            }
        }

        // ── Phase 4: Coordinator produces final synthesis ─────────────────────────
        yield return AgentEvent.SystemNote("Phase 4: Coordinator producing final synthesis from all findings");
        yield return AgentEvent.Thinking(coordinator.Name, coordinator.Colour);

        var finalPrompt = $"""
            Produce a final incident response summary based on all specialist findings.

            INCIDENT:
            {userPrompt}

            ALL SPECIALIST FINDINGS:
            {findings}
            """;

        var finalSummary = await coordinator.GetResponseAsync(finalPrompt, cancellationToken);
        yield return AgentEvent.Aggregate(finalSummary);

        yield return AgentEvent.Complete($"✅ Magentic orchestration complete — {iteration} tasks executed with dynamic planning.");
    }

    private static List<(string Agent, string Description)> ParseTaskLedger(string ledgerText)
    {
        var tasks = new List<(string Agent, string Description)>();

        foreach (var line in ledgerText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("TASK_", StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith("NEW_TASK_", StringComparison.OrdinalIgnoreCase)) continue;

            // Format: TASK_N: [agent: XXX] description
            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx < 0) continue;

            var rest = trimmed.Substring(colonIdx + 1).Trim();

            // Try to extract [agent: xxx]
            string agent = "safety"; // default
            string description = rest;

            var agentMatch = System.Text.RegularExpressions.Regex.Match(rest, @"\[agent:\s*(safety|security|facilities)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (agentMatch.Success)
            {
                agent = agentMatch.Groups[1].Value.ToLowerInvariant();
                description = rest.Replace(agentMatch.Value, "").Trim();
            }
            else
            {
                // Fallback: look for keyword mentions
                var lowerRest = rest.ToLowerInvariant();
                if (lowerRest.Contains("security") || lowerRest.Contains("access") || lowerRest.Contains("cctv")) agent = "security";
                else if (lowerRest.Contains("facilities") || lowerRest.Contains("building") || lowerRest.Contains("structural") || lowerRest.Contains("ammonia") || lowerRest.Contains("plant")) agent = "facilities";
                else agent = "safety";
            }

            if (!string.IsNullOrWhiteSpace(description))
                tasks.Add((agent, description));
        }

        return tasks;
    }
}

internal sealed class ManagerAgent(Kernel kernel) : AssistAgent(kernel)
{
    public override string Name   => "Manager Agent";
    public override string Colour => "#8b5cf6";
    public override string Role   => "Orchestrator / Planner";

    protected override string SystemPrompt => """
        You are the Incident Response Manager for a multi-agent response system.
        You do NOT directly analyse incidents — instead, you plan and coordinate
        the work of specialist agents (Safety, Security, Facilities).

        Your responsibilities:
        1. Analyse the incident to understand what domains are affected
        2. Create a prioritised task ledger of investigation tasks for specialist agents
        3. Review findings and identify if additional investigation is needed
        4. Adapt the plan when new information changes the picture

        Be strategic, not operational. Think about what you need to know and who can find it out.
        Always consider: are there any hidden risks not immediately obvious from the description?
        """;
}
