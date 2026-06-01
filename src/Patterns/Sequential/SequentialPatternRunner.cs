using System.Runtime.CompilerServices;
using System.Text;
using Agents;
using Core.Infrastructure;
using Core.Interfaces;
using Core.Models;
using Microsoft.SemanticKernel;

namespace Patterns.Sequential;

/// <summary>
/// PATTERN 1 — SEQUENTIAL (Pipeline / Prompt Chaining)
///
/// Each agent processes the output of the previous one in a fixed, linear order.
/// No agent runs until the previous has finished.
///
/// Scenario: Incident Documentation Pipeline
///   Step 1 — Capture Agent:    Extracts structured details from a freeform incident description
///   Step 2 — Safety Agent:     Assesses risk and immediate safety actions
///   Step 3 — Compliance Agent: Checks regulatory reporting obligations
///   Step 4 — Report Agent:     Generates a formal incident report
///
/// ✅ PROS:
///   - Simple to reason about — clear order of operations
///   - Each step builds on and refines the previous output
///   - Predictable, deterministic flow
///   - Easy to debug (log each step)
///
/// ❌ CONS:
///   - Inherently slow — each agent must wait for the previous to finish
///   - A failure or poor-quality output early on cascades through the pipeline
///   - No parallelism, so does not scale well to many agents
/// </summary>
public sealed class SequentialPatternRunner : IPatternRunner
{
    private readonly Kernel _kernel;

    public string PatternId => "sequential";

    public PatternInfo Info => new(
        Id:                  "sequential",
        Name:                "Sequential",
        Icon:                "🔗",
        ShortDescription:    "Linear pipeline — each agent builds on the last",
        DetailedDescription: "Agents execute in a fixed, predefined order. Each agent receives the output of the previous agent as its input, progressively refining and enriching the result.",
        ScenarioTitle:       "Incident Documentation Pipeline",
        ScenarioDescription: "A routine near-miss runs through a fixed 4-stage pipeline. Watch how every stage must finish before the next begins — even for a simple, single-domain incident that only Safety needs to touch. Compare the total time to parallel patterns.",
        DefaultPrompt:       "A colleague reported a near-miss slip on a wet floor near the bakery section. No injuries were sustained. The floor had been mopped and a wet floor sign was in place, but the sign had fallen over. Two colleagues witnessed the incident.",
        Pros:                ["Simple, predictable flow", "Each step enriches the previous output", "Easy to debug step-by-step", "Natural fit for document generation workflows"],
        Cons:                ["No parallelism — inherently slow", "Early-stage failures cascade forward", "All agents must complete even if some steps aren't needed"],
        AgentsInvolved:      ["Incident Capture Agent", "Safety Specialist", "Compliance Agent", "Report Generation Agent"]
    );

    public SequentialPatternRunner(Kernel kernel)
    {
        _kernel = kernel;
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        string userPrompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return AgentEvent.SystemNote("▶ Starting Sequential Pipeline — 4 stages");

        // ── Stage 1: Incident Capture ────────────────────────────────────────────
        // Accumulate the full text from streaming events — avoids a second LLM call per stage.
        yield return AgentEvent.SystemNote("Stage 1/4 — Incident Capture Agent");
        var captureAgent = new IncidentCaptureAgent(_kernel);
        var capturedDetailsSb = new StringBuilder();
        await foreach (var evt in captureAgent.InvokeStreamingAsync(userPrompt, cancellationToken))
        {
            if (evt.EventType == AgentEventType.AgentResponse) capturedDetailsSb.Append(evt.Content);
            yield return evt;
        }
        var capturedDetails = capturedDetailsSb.ToString();

        // ── Stage 2: Safety Assessment ───────────────────────────────────────────
        yield return AgentEvent.SystemNote("Stage 2/4 — Safety Specialist (using Stage 1 output as input)");
        var safetyAgent = new SafetySpecialistAgent(_kernel);
        var safetyInput = $"Structured incident details:\n{capturedDetails}\n\nProvide a safety assessment.";
        var safetyAssessmentSb = new StringBuilder();
        await foreach (var evt in safetyAgent.InvokeStreamingAsync(safetyInput, cancellationToken))
        {
            if (evt.EventType == AgentEventType.AgentResponse) safetyAssessmentSb.Append(evt.Content);
            yield return evt;
        }
        var safetyAssessment = safetyAssessmentSb.ToString();

        // ── Stage 3: Compliance Check ────────────────────────────────────────────
        yield return AgentEvent.SystemNote("Stage 3/4 — Compliance Agent (using Stage 2 output as input)");
        var complianceAgent = new ComplianceAgent(_kernel);
        var complianceInput = $"Incident details:\n{capturedDetails}\n\nSafety assessment:\n{safetyAssessment}\n\nWhat are the regulatory reporting obligations?";
        var complianceNotesSb = new StringBuilder();
        await foreach (var evt in complianceAgent.InvokeStreamingAsync(complianceInput, cancellationToken))
        {
            if (evt.EventType == AgentEventType.AgentResponse) complianceNotesSb.Append(evt.Content);
            yield return evt;
        }
        var complianceNotes = complianceNotesSb.ToString();

        // ── Stage 4: Report Generation ───────────────────────────────────────────
        yield return AgentEvent.SystemNote("Stage 4/4 — Report Generation Agent (synthesising all previous stages)");
        var reportAgent = new ReportGenerationAgent(_kernel);

        var reportInput = $"""
            Please generate a formal incident report using all of the following inputs:

            ORIGINAL DESCRIPTION:
            {userPrompt}

            STRUCTURED DETAILS (Stage 1):
            {capturedDetails}

            SAFETY ASSESSMENT (Stage 2):
            {safetyAssessment}

            COMPLIANCE & REPORTING (Stage 3):
            {complianceNotes}
            """;

        await foreach (var evt in reportAgent.InvokeStreamingAsync(reportInput, cancellationToken))
            yield return evt;

        yield return AgentEvent.Complete("✅ Sequential pipeline complete — 4 agents executed in series.");
    }
}

// ── Supporting agents (internal to this pattern) ───────────────────────────────

internal sealed class IncidentCaptureAgent(Kernel kernel) : AssistAgent(kernel)
{
    public override string Name   => "Incident Capture Agent";
    public override string Colour => "#f59e0b";
    public override string Role   => "Data Extraction";

    protected override string SystemPrompt => """
        You are an Incident Capture Agent. Your sole job is to extract and structure
        the key facts from a freeform incident description.

        Output a structured summary with these fields:
        - INCIDENT TYPE: (accident / near-miss / hazard / enforcement)
        - DATE/TIME: (if mentioned, otherwise "Not specified")
        - LOCATION: (exact location within the site)
        - PERSONS INVOLVED: (names/roles if mentioned)
        - WITNESSES: (number and names if mentioned)
        - WHAT HAPPENED: (factual, chronological summary)
        - IMMEDIATE ACTIONS TAKEN: (if any)
        - INJURIES/DAMAGE: (describe or "None reported")

        Be factual only — no analysis or recommendations at this stage.
        """;
}

internal sealed class ComplianceAgent(Kernel kernel) : AssistAgent(kernel)
{
    public override string Name   => "Compliance Agent";
    public override string Colour => "#06b6d4";
    public override string Role   => "Regulatory Compliance";

    protected override string SystemPrompt => """
        You are a Regulatory Compliance Specialist for a UK retail organisation.
        You check incidents against UK health and safety legislation.

        For the incident provided, determine:
        - Is this RIDDOR reportable? (Reporting of Injuries, Diseases and Dangerous Occurrences Regulations 2013)
          If so, what type of RIDDOR report is required and within what timeframe?
        - Are there any other statutory reporting obligations? (HSE, local authority, insurer)
        - What records must be kept and for how long?
        - Are there any COSHH implications? (Control of Substances Hazardous to Health)
        - Any other relevant legislation: Manual Handling, Working at Height, PUWER, etc.

        Be specific about reporting timescales and legal obligations.
        """;
}

internal sealed class ReportGenerationAgent(Kernel kernel) : AssistAgent(kernel)
{
    public override string Name   => "Report Generation Agent";
    public override string Colour => "#84cc16";
    public override string Role   => "Report Generation";

    protected override string SystemPrompt => """
        You are a Report Generation Agent. Using all provided inputs, produce a
        formal, professional Incident Report in the following structure:

        # INCIDENT REPORT

        ## Section 1: Incident Summary
        ## Section 2: Persons Involved
        ## Section 3: Risk Assessment
        ## Section 4: Immediate Actions Taken
        ## Section 5: Regulatory & Reporting Obligations
        ## Section 6: Recommended Preventive Measures
        ## Section 7: Sign-off Requirements

        The report should be suitable for submission to management, HSE, and insurers.
        Use formal language. Include severity rating: Low / Medium / High / Critical.
        """;
}
