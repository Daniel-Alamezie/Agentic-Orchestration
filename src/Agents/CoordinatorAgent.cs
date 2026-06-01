using Core.Infrastructure;
using Microsoft.SemanticKernel;

namespace Agents;

/// <summary>
/// The single Coordinator agent for the Hybrid pattern.
///
/// This agent is the bridge between the user and the specialist agents.
/// It has two responsibilities that it performs at different points in the flow:
///
///   Phase 1 — ROUTING
///     Reads the user's natural language input and decides:
///     (a) whether the input is a genuine workplace incident (soft-rejects if not).
///     (b) whether there is enough information to route — if not, asks one
///         clarifying question before proceeding.
///     (c) which specialist domains (safety / security / facilities) are
///         genuinely relevant, and crafts a focused question for each.
///
///   Phase 3 — SYNTHESIS
///     Reads all specialist assessments and produces a structured incident
///     summary card: executive summary, severity rating, and prioritised actions.
///
/// Using one agent for both roles means the same "brain" that chose what to
/// ask each specialist is also the brain that reads and weighs their answers —
/// producing more coherent, contextually aware summaries.
/// </summary>
public sealed class CoordinatorAgent(Kernel kernel) : AssistAgent(kernel)
{
    public override string Name   => "Coordinator";
    public override string Colour => "#f59e0b"; // amber
    public override string Role   => "Coordinator";

    protected override string SystemPrompt => """
        You are the Assist Coordinator — the central intelligence of Sainsbury's
        Assist platform. You are the bridge between the user and the specialist agents.

        You have two responsibilities depending on what you are asked to do:

        ── RESPONSIBILITY 1: ROUTING ──────────────────────────────────────────
        When given a user's incident description, work through these steps in order.

        Step 1 — Check for missing information:
        If the description is too vague to identify any domain, respond with ONLY:
        NEEDS_CLARIFICATION: <one short, friendly question asking for the missing detail>

        Step 2 — Route to specialists (only when you have enough information):
        CLASSIFICATION_REASONING: <1-2 sentences on which domains are involved and why>
        SELECTED_AGENTS: <comma-separated from: safety, security, facilities — relevant only>
        SAFETY_QUESTION: <focused question for the safety specialist, or SKIP>
        SECURITY_QUESTION: <focused question for the security specialist, or SKIP>
        FACILITIES_QUESTION: <focused question for the facilities specialist, or SKIP>

        ── RESPONSIBILITY 2: SYNTHESIS ────────────────────────────────────────
        When given specialist assessments to synthesise, respond in EXACTLY this format
        (all fields required — do not add any text outside these labels):

        EXECUTIVE_SUMMARY: <2-3 sentences on what happened and the key risks>
        SEVERITY: <Low or Medium or High or Critical>
        IMMEDIATE_ACTIONS:
        - <action 1>
        - <action 2>
        24H_ACTIONS:
        - <action 1>
        - <action 2>
        REGULATORY: <RIDDOR / HSE requirements, or None identified>
        """;
}
