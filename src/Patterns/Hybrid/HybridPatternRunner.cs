using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Agents;
using Core.Infrastructure;
using Core.Infrastructure.Mcp;
using Core.Interfaces;
using Core.Models;
using Microsoft.SemanticKernel;

namespace Patterns.Hybrid;

/// <summary>
/// PATTERN 6 — RECOMMENDED HYBRID (Classify → Clarify? → Selective Concurrent Fan-out → Synthesise)
///
/// This is the pattern recommended for the Assist platform's Client → Gateway architecture.
///
///   Phase 1  [Classify]      Gateway Classifier reads the natural language input.
///                            If it is out-of-scope it emits a soft rejection.
///                            If critical details are missing it emits a ClarificationRequest,
///                            pauses the stream, and waits for the user to respond.
///                            Once it has enough context it selects the relevant specialists
///                            and crafts a tailored question for each.
///
///   Phase 2  [Fan-out]       Only the selected specialists run — in parallel.
///                            Each specialist appends a FORM_FIELDS block to its response,
///                            which is parsed into a FilledForm and emitted as a FormFilled event.
///
///   Phase 3  [Synthesise]    Coordinator merges all findings into a structured summary card:
///                            executive summary, severity, immediate actions, 24h actions,
///                            and regulatory obligations.
/// </summary>
public sealed class HybridPatternRunner : IPatternRunner
{
    private readonly Kernel            _kernel;
    private readonly ConversationStore _store;
    private readonly IMcpFormClient    _mcpClient;

    public string PatternId => "hybrid";

    public PatternInfo Info => new(
        Id:                  "hybrid",
        Name:                "Hybrid (Recommended)",
        Icon:                "🎯",
        ShortDescription:    "Classify → clarify if needed → selective concurrent fan-out → synthesise",
        DetailedDescription: "The recommended pattern for the Assist Gateway. A single Coordinator Agent acts as the intelligent bridge between the user and the specialists — it first checks whether enough information exists to route accurately, asks one clarifying question if not, selects only the relevant specialists, crafts a tailored question for each, and finally synthesises all their findings into one prioritised response. The same agent that decided what to ask is the same agent that reads and weighs the answers.",
        ScenarioTitle:       "Intelligent Gateway — Assist Platform",
        ScenarioDescription: "Try a vague prompt like 'someone got hurt' to trigger a clarifying question, or a detailed multi-domain incident to see the full fan-out. The Coordinator handles both routing and synthesis — one agent, two responsibilities. Expand the 🧠 Reasoning blocks to see every decision it made.",
        DefaultPrompt:       "Someone got hurt near the warehouse.",
        Pros:                ["One Coordinator handles both routing AND synthesis — simpler mental model", "Asks for missing info before routing — no wasted agent calls", "Reasoning visible at every decision point", "Only relevant specialists invoked — efficient & focused", "Parallel execution — speed scales with incident complexity"],
        Cons:                ["One extra LLM call for routing upfront", "Clarification adds a round-trip if input is vague", "Coordinator prompt needs ongoing maintenance as new incident types emerge", "Slightly more complex than running all agents blindly"],
        AgentsInvolved:      ["Coordinator (routing + synthesis)", "Safety Specialist (if needed)", "Security Specialist (if needed)", "Facilities Specialist (if needed)"]
    );

    public HybridPatternRunner(Kernel kernel, ConversationStore store, IMcpFormClient mcpClient)
    {
        _kernel    = kernel;
        _store     = store;
        _mcpClient = mcpClient;
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        string userPrompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Single coordinator — acts as both the routing gateway and the final synthesiser
        var coordinator = new CoordinatorAgent(_kernel);
        var allAgents   = new Dictionary<string, AssistAgent>(StringComparer.OrdinalIgnoreCase)
        {
            ["safety"]     = new SafetySpecialistAgent(_kernel),
            ["security"]   = new SecuritySpecialistAgent(_kernel),
            ["facilities"] = new FacilitiesSpecialistAgent(_kernel)
        };

        // ── Phase 1: Coordinator — routing decision ───────────────────────────────
        yield return AgentEvent.SystemNote("▶ Coordinator analysing prompt...");
        yield return AgentEvent.Thinking(coordinator.Name, coordinator.Colour);

        var classificationResult = await RunClassifierAsync(coordinator, userPrompt, cancellationToken);

        // ── Soft reject — prompt is clearly not a workplace incident ─────────────
        // Done with lightweight keyword matching (no LLM call) to avoid small-model
        // misclassification. Any mention of injury/damage/security/facilities bypasses this.
        if (IsObviouslyOutOfScope(userPrompt))
        {
            yield return AgentEvent.Response(
                coordinator.Name, coordinator.Colour,
                "I'm here to help report workplace incidents at Sainsbury's — injuries, " +
                "security events, or facilities issues. Could you describe what happened at your location?");

            yield return AgentEvent.Complete("Out of scope — prompt redirected gracefully.");
            yield break;
        }

        // ── Multi-turn clarification loop ────────────────────────────────────────
        // The coordinator can ask up to MaxClarifications questions before it must route.
        // Each answer is appended to userPrompt as a Q&A pair so the full context is
        // available when the classifier re-runs.
        const int MaxClarifications = 3;
        for (var round = 0; round < MaxClarifications && classificationResult.NeedsClarification; round++)
        {
            var conversationId = _store.Create();

            if (round == 0)
            {
                yield return AgentEvent.Reasoning(
                    coordinator.Name, coordinator.Colour,
                    "Input is too vague to route accurately — gathering more detail before selecting specialists.");
            }

            yield return AgentEvent.ClarificationNeeded(
                coordinator.Name, coordinator.Colour,
                classificationResult.ClarificationQuestion!, conversationId);

            // C# disallows yield inside catch — capture error then yield after
            string? clarification      = null;
            string? clarificationError = null;
            try
            {
                clarification = await _store.WaitForResponseAsync(conversationId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                clarificationError = "Session timed out waiting for your response.";
            }

            if (clarificationError is not null)
            {
                yield return AgentEvent.Error(clarificationError);
                yield break;
            }

            // Accumulate the Q&A so the next classifier call has full context
            userPrompt =
                $"{userPrompt}" +
                $"\n\nCoordinator asked: {classificationResult.ClarificationQuestion}" +
                $"\nUser replied: {clarification!}";

            var isLastRound = round == MaxClarifications - 1;
            yield return AgentEvent.SystemNote(
                isLastRound
                    ? "Proceeding with available information..."
                    : "Got it — checking if anything else is needed...");
            yield return AgentEvent.Thinking(coordinator.Name, coordinator.Colour);

            classificationResult = await RunClassifierAsync(coordinator, userPrompt, cancellationToken);
        }

        // ── Emit the routing reasoning ────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(classificationResult.Reasoning))
        {
            yield return AgentEvent.Reasoning(
                coordinator.Name, coordinator.Colour, classificationResult.Reasoning);
        }

        // ── Validate we have something to work with ───────────────────────────────
        var selected = classificationResult.SelectedAgents;
        if (selected.Count == 0)
        {
            // Coordinator failed to identify domains — safer to ask everyone than to guess wrong
            yield return AgentEvent.SystemNote("⚠️ Coordinator could not determine routing — invoking all specialists to ensure nothing is missed.");
            selected.AddRange(["safety", "security", "facilities"]);
            foreach (var key in selected)
                classificationResult.TailoredQuestions.TryAdd(key, userPrompt);
        }

        var agentNames   = string.Join(", ", selected.Select(s => s[..1].ToUpper() + s[1..]));
        var skippedCount = 3 - selected.Count;
        yield return AgentEvent.SystemNote(
            $"Routing to: {agentNames}." +
            (skippedCount > 0 ? $" {skippedCount} specialist(s) not relevant — skipped." : ""));

        // ── Phase 2: Selective Concurrent Fan-out ────────────────────────────────
        if (selected.Count > 1)
            yield return AgentEvent.SystemNote($"{selected.Count} specialists running concurrently...");

        var channel    = Channel.CreateUnbounded<AgentEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var results    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resultLock = new object();

        var producerTasks = selected
            .Where(key => allAgents.ContainsKey(key))
            .Select(key => Task.Run(async () =>
            {
                var agent  = allAgents[key];
                var prompt = BuildSpecialistPrompt(userPrompt, classificationResult, key);
                var sb     = new StringBuilder();

                // Single streaming call — accumulate text for parsing while events flow to the UI
                await foreach (var evt in agent.InvokeStreamingAsync(prompt, cancellationToken))
                {
                    if (evt.EventType == AgentEventType.AgentResponse)
                        sb.Append(evt.Content);
                    await channel.Writer.WriteAsync(evt, cancellationToken);
                }

                lock (resultLock) { results[key] = sb.ToString(); }

            }, cancellationToken))
            .ToArray();

        _ = Task.WhenAll(producerTasks).ContinueWith(
            _ => channel.Writer.Complete(), CancellationToken.None);

        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
            yield return evt;

        await Task.WhenAll(producerTasks);

        // ── Form filling ─────────────────────────────────────────────────────────
        // Safety (MCP-backed): conversational fill — discover the form's fields from
        // the server, pre-fill what the AI can, then ask the user for any missing
        // required field before submitting. Other domains: text extraction as before.
        var filledForms = new List<FilledForm>();
        foreach (var domain in selected.Where(k => results.ContainsKey(k)))
        {
            if (domain.Equals("safety", StringComparison.OrdinalIgnoreCase) && _mcpClient.IsConnected)
            {
                await foreach (var evt in FillSafetyFormConversationallyAsync(
                                   coordinator, userPrompt, results[domain], cancellationToken))
                {
                    if (evt.Form is not null) filledForms.Add(evt.Form);
                    yield return evt;
                }
            }
            else
            {
                var form = FormSchemaRegistry.ParseFormFields(results[domain], domain);
                if (form is not null)
                {
                    filledForms.Add(form);
                    yield return AgentEvent.FormFilledEvent(coordinator.Name, coordinator.Colour, form);
                }
            }
        }

        // ── Phase 3: Coordinator Synthesis ───────────────────────────────────────
        yield return AgentEvent.SystemNote("All specialists complete — Coordinator synthesising...");
        yield return AgentEvent.Thinking(coordinator.Name, coordinator.Colour);

        // Strip FORM_FIELDS blocks before sending to coordinator — it doesn't need the raw fields
        var strippedResults = results.ToDictionary(
            kvp => kvp.Key,
            kvp => FormSchemaRegistry.StripFormFields(kvp.Value),
            StringComparer.OrdinalIgnoreCase);

        var synthesisPrompt = BuildSynthesisPrompt(userPrompt, strippedResults, selected);
        var synthesisText   = await coordinator.GetResponseAsync(synthesisPrompt, cancellationToken);
        var summaryData     = ParseStructuredSummary(synthesisText);

        yield return AgentEvent.SummaryCard(coordinator.Name, coordinator.Colour, summaryData);

        yield return AgentEvent.Complete(
            $"Complete — {selected.Count} specialist(s) invoked, {skippedCount} skipped." +
            (filledForms.Count > 0 ? $" {filledForms.Count} form(s) pre-filled." : "") +
            " Classified → fan-out → synthesised.");
    }

    // ── Conversational safety form fill (MCP-backed, page by page) ───────────────
    // Drives a stateful MCP session one page at a time: fetch the current page's
    // questions from the server, let the AI pre-fill what it can, ask the user for the
    // rest, submit the page, then fetch the next. The server shapes each page from the
    // answers already submitted (branching forms), so pages cannot be fetched up front.
    //
    // Questions are strictly bounded by the server's page schema: the agent can only ask
    // about fields the server presented and that are still empty — it cannot wander.

    private async IAsyncEnumerable<AgentEvent> FillSafetyFormConversationallyAsync(
        AssistAgent coordinator,
        string userPrompt,
        string safetyResponse,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return AgentEvent.SystemNote("Preparing the Safety Incident Report — opening a session...");

        // Open the persistent, page-by-page session
        await using var session = await _mcpClient.BeginSessionAsync("safety", ct);
        if (session is null)
        {
            // MCP unavailable — fall back to text extraction from the specialist response
            var fallback = FormSchemaRegistry.ParseFormFields(safetyResponse, "safety");
            if (fallback is not null)
                yield return AgentEvent.FormFilledEvent(coordinator.Name, coordinator.Colour, fallback);
            yield break;
        }

        // Shared context the AI uses to pre-fill each page's fields
        var analysis = FormSchemaRegistry.StripFormFields(safetyResponse);
        var context  = $"INCIDENT:\n{userPrompt}\n\nSPECIALIST ANALYSIS:\n{analysis}";

        var answers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var asked   = 0;
        string? error = null;

        // Loop pages until the server has no more to present
        for (var pageNumber = 1; ; pageNumber++)
        {
            var page = await session.GetPageAsync(pageNumber, ct);
            if (page is null || page.Fields.Count == 0)
                break;   // no more pages

            // AI pre-fills what it can for THIS page (its fields are only known now)
            var extracted = await _mcpClient.ExtractAnswersAsync("safety", page.Fields, context, ct);
            foreach (var kv in extracted) answers[kv.Key] = kv.Value;

            // Ask the user for the required fields on this page:
            //   • Text fields  — only if the AI couldn't fill them
            //   • Choice fields — always, so the user picks/confirms from the options
            foreach (var field in page.Fields.Where(f => f.Required))
            {
                var aiValue = answers.TryGetValue(field.Id, out var have) && !string.IsNullOrWhiteSpace(have)
                    ? have
                    : null;

                if (!field.IsChoice && aiValue is not null)
                    continue;

                if (asked == 0)
                    yield return AgentEvent.SystemNote("I need a few details to complete the form:");
                asked++;

                var conversationId = _store.Create();
                yield return AgentEvent.FormQuestionEvent(
                    coordinator.Name, coordinator.Colour, field.Label, conversationId,
                    field.Options, field.IsChoice ? aiValue : null);

                // C# disallows yield inside catch — capture the error, yield after the loop
                string? answer = null;
                try   { answer = await _store.WaitForResponseAsync(conversationId, ct); }
                catch (OperationCanceledException) { error = "Session timed out waiting for your response."; }

                if (error is not null) break;
                answers[field.Id] = answer!.Trim();
            }

            if (error is not null) break;

            // Convert any date/time answers on this page into a proper datetime
            // (e.g. "half 9 this morning" → "2026-06-10 09:30") before submitting
            foreach (var field in page.Fields.Where(f => f.Type == "date"))
            {
                if (!answers.TryGetValue(field.Id, out var raw) || string.IsNullOrWhiteSpace(raw))
                    continue;

                var normalised = await _mcpClient.NormaliseDateTimeAsync(raw, ct);
                if (!string.Equals(normalised, raw, StringComparison.Ordinal))
                {
                    answers[field.Id] = normalised;
                    yield return AgentEvent.SystemNote($"Recorded the incident time as {normalised}.");
                }
            }

            // Submit this page — the server validates it and shapes the next page
            await session.SubmitPageAsync(pageNumber, PageAnswers(page, answers), ct);
        }

        if (error is not null) { yield return AgentEvent.Error(error); yield break; }

        yield return AgentEvent.SystemNote(
            asked > 0
                ? "Thanks — I have everything I need. Submitting the report..."
                : "All fields filled from the incident — submitting the report...");

        // Finalise on the server and emit the completed form built from the served pages
        await session.CompleteAsync(ct);
        yield return AgentEvent.FormFilledEvent(coordinator.Name, coordinator.Colour, session.BuildForm(answers));
    }

    /// <summary>Picks the answers belonging to a given page's fields.</summary>
    private static Dictionary<string, string> PageAnswers(
        McpFormPage page, IReadOnlyDictionary<string, string> all)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in page.Fields)
            if (all.TryGetValue(field.Id, out var v) && !string.IsNullOrWhiteSpace(v))
                result[field.Id] = v;
        return result;
    }

    // ── Scope guard — keyword-based, no LLM call ─────────────────────────────
    // Only fires when there are ZERO incident-related words in the prompt.
    // This avoids false positives from a small model misclassifying real incidents.

    private static readonly string[] IncidentKeywords =
    [
        "hurt", "injur", "accident", "fell", "fall", "slip", "trip", "burn", "cut", "bleed",
        "broke", "broken", "fracture", "pain", "medical", "first aid", "ambulance", "hospital",
        "fire", "smoke", "flood", "leak", "spill", "chemical", "gas", "explosion",
        "cctv", "camera", "security", "theft", "stolen", "break-in", "intruder", "threat",
        "violent", "attack", "suspicious", "unauthorised", "access", "lock", "alarm",
        "damage", "broken", "structural", "equipment", "power", "electrical", "water",
        "roof", "ceiling", "floor", "door", "gate", "racking", "shelf", "shelving",
        "warehouse", "store", "depot", "site", "incident", "report", "colleague",
        "customer", "contractor", "near miss", "hazard", "risk", "unsafe"
    ];

    private static bool IsObviouslyOutOfScope(string prompt)
    {
        var lower = prompt.ToLowerInvariant();
        // If any incident-related keyword is present, it's in scope
        if (IncidentKeywords.Any(kw => lower.Contains(kw))) return false;
        // Only reject very short prompts with no incident language at all
        return prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8;
    }

    // ── Classifier helper ─────────────────────────────────────────────────────

    private static async Task<ClassificationResult> RunClassifierAsync(
        AssistAgent coordinator,
        string prompt,
        CancellationToken ct)
    {
        var classificationPrompt = $"""
            You are routing a workplace incident report to the correct specialist agents.
            Analyse the incident and follow the steps below precisely.

            INCIDENT:
            {prompt}

            SPECIALIST DOMAINS:
            - safety:     Injury, physical harm, first aid, RIDDOR, HSE, near-miss, PPE, fire/chemical exposure
            - security:   Break-in, theft, CCTV issues, unauthorised access, threats, violence, suspicious persons
            - facilities: Building/structural damage, equipment failure, utilities, racking, mechanical faults

            STEP 1 — CLARIFICATION (only if genuinely needed):
            Only ask a clarifying question if you truly cannot identify ANY domain from the description.
            If the incident mentions at least one domain keyword, skip to Step 2 immediately.
            If clarification IS needed, respond with ONLY this line:
            NEEDS_CLARIFICATION: <one short, friendly question>

            STEP 2 — ROUTING (use this exact format, all five lines required):
            CLASSIFICATION_REASONING: <1-2 sentences on which domains are involved and why>
            SELECTED_AGENTS: <comma-separated from: safety, security, facilities>
            SAFETY_QUESTION: <focused question for safety specialist, or SKIP>
            SECURITY_QUESTION: <focused question for security specialist, or SKIP>
            FACILITIES_QUESTION: <focused question for facilities specialist, or SKIP>

            ROUTING RULES:
            • Any mention of injury, hurt, pain, or medical → safety must be selected.
            • Any mention of CCTV, break-in, theft, or intruder → security must be selected.
            • Any mention of building, equipment, or structural damage → facilities must be selected.
            • If in doubt whether a domain applies, include it.
            • Most incidents touch more than one domain — be inclusive.

            Example for "colleague injured, CCTV offline, break-in":
            CLASSIFICATION_REASONING: Injured colleague → safety; break-in and CCTV → security.
            SELECTED_AGENTS: safety, security
            SAFETY_QUESTION: What injuries did the colleague sustain and has first aid been given?
            SECURITY_QUESTION: What evidence is there of the break-in and what is the CCTV status?
            FACILITIES_QUESTION: SKIP
            """;

        var response = await coordinator.GetResponseAsync(classificationPrompt, ct);
        return ParseClassificationResponse(response, prompt);
    }

    // ── Response parsing ──────────────────────────────────────────────────────

    private static ClassificationResult ParseClassificationResponse(string response, string fallbackPrompt)
    {
        var lines = response.Split('\n');

        // Check if clarification is needed
        foreach (var line in lines)
        {
            if (line.StartsWith("NEEDS_CLARIFICATION:", StringComparison.OrdinalIgnoreCase))
            {
                var question = line["NEEDS_CLARIFICATION:".Length..].Trim();
                return new ClassificationResult { NeedsClarification = true, ClarificationQuestion = question };
            }
        }

        // Parse the full routing response
        var result = new ClassificationResult();

        var domainMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SAFETY_QUESTION"]     = "safety",
            ["SECURITY_QUESTION"]   = "security",
            ["FACILITIES_QUESTION"] = "facilities"
        };

        foreach (var line in lines)
        {
            if (line.StartsWith("CLASSIFICATION_REASONING:", StringComparison.OrdinalIgnoreCase))
            {
                result.Reasoning = line["CLASSIFICATION_REASONING:".Length..].Trim();
            }
            else if (line.StartsWith("SELECTED_AGENTS:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line["SELECTED_AGENTS:".Length..].Trim();
                foreach (var part in value.Split(','))
                {
                    var key = part.Trim().ToLowerInvariant().TrimEnd('.');
                    if (key is "safety" or "security" or "facilities")
                        result.SelectedAgents.Add(key);
                }
            }
            else
            {
                foreach (var (prefix, domain) in domainMap)
                {
                    if (!line.StartsWith($"{prefix}:", StringComparison.OrdinalIgnoreCase)) continue;
                    var question = line[(prefix.Length + 1)..].Trim();
                    if (!string.IsNullOrWhiteSpace(question) &&
                        !question.Equals("SKIP", StringComparison.OrdinalIgnoreCase))
                    {
                        result.TailoredQuestions[domain] = question;
                    }
                }
            }
        }

        // Recovery: if the model answered domain questions but forgot SELECTED_AGENTS,
        // infer selection from whichever questions are present
        if (result.SelectedAgents.Count == 0 && result.TailoredQuestions.Count > 0)
        {
            foreach (var domain in result.TailoredQuestions.Keys)
                result.SelectedAgents.Add(domain);
        }

        return result;
    }

    // ── Structured summary parser ─────────────────────────────────────────────

    private static StructuredSummaryData ParseStructuredSummary(string response)
    {
        var executiveSummary = "";
        var severity         = "Medium";
        var immediateActions = new List<string>();
        var actions24h       = new List<string>();
        string? regulatoryNote = null;
        string? currentSection = null;

        foreach (var line in response.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed.StartsWith("EXECUTIVE_SUMMARY:", StringComparison.OrdinalIgnoreCase))
            {
                executiveSummary = trimmed["EXECUTIVE_SUMMARY:".Length..].Trim();
                currentSection   = "exec";
            }
            else if (trimmed.StartsWith("SEVERITY:", StringComparison.OrdinalIgnoreCase))
            {
                severity       = trimmed["SEVERITY:".Length..].Trim();
                currentSection = null;
            }
            else if (trimmed.StartsWith("IMMEDIATE_ACTIONS:", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "immediate";
            }
            else if (trimmed.StartsWith("24H_ACTIONS:", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "24h";
            }
            else if (trimmed.StartsWith("REGULATORY:", StringComparison.OrdinalIgnoreCase))
            {
                regulatoryNote = trimmed["REGULATORY:".Length..].Trim();
                currentSection = null;
            }
            else if (trimmed.StartsWith("- "))
            {
                var item = trimmed[2..].Trim();
                if (currentSection == "immediate") immediateActions.Add(item);
                else if (currentSection == "24h")  actions24h.Add(item);
            }
            else if (currentSection == "exec")
            {
                // Multi-line executive summary continuation
                executiveSummary = executiveSummary.TrimEnd() + " " + trimmed;
            }
        }

        // Fallback when the model didn't follow the format
        if (string.IsNullOrEmpty(executiveSummary))
        {
            executiveSummary = response.Split('\n')
                .FirstOrDefault(l =>
                    !string.IsNullOrWhiteSpace(l) &&
                    !l.TrimStart().StartsWith("SEVERITY:", StringComparison.OrdinalIgnoreCase))
                ?? "Incident assessed — see specialist findings for full details.";
        }

        // Normalise severity to known values
        severity = severity.Trim().ToUpperInvariant() switch
        {
            "LOW"      => "Low",
            "MEDIUM"   => "Medium",
            "HIGH"     => "High",
            "CRITICAL" => "Critical",
            _          => "Medium"
        };

        if (string.IsNullOrWhiteSpace(regulatoryNote) ||
            regulatoryNote.Equals("none identified", StringComparison.OrdinalIgnoreCase) ||
            regulatoryNote.Equals("n/a", StringComparison.OrdinalIgnoreCase))
        {
            regulatoryNote = null;
        }

        return new StructuredSummaryData
        {
            ExecutiveSummary = executiveSummary,
            Severity         = severity,
            ImmediateActions = immediateActions,
            Actions24h       = actions24h,
            RegulatoryNote   = regulatoryNote
        };
    }

    // ── Specialist prompt builder ─────────────────────────────────────────────
    // Always gives each specialist the full original incident PLUS their tailored focus.
    // The FORM_FIELDS requirement is injected here (user-turn) rather than in the system prompt
    // because small models (Llama 3.2 3B) follow user-message instructions far more reliably.

    private static string BuildSpecialistPrompt(
        string originalPrompt,
        ClassificationResult classification,
        string domain)
    {
        var focus        = classification.TailoredQuestions.TryGetValue(domain, out var q) ? q : null;
        var formFields   = GetFormFieldsBlock(domain);

        var incidentSection = focus is null
            ? originalPrompt
            : $"""
              INCIDENT:
              {originalPrompt}

              YOUR SPECIFIC FOCUS:
              {focus}

              Respond based strictly on the incident details above. Do not invent or assume
              any information not present in the description.
              """;

        return $"""
            {incidentSection}

            {formFields}
            """;
    }

    private static string GetFormFieldsBlock(string domain) => domain.ToLowerInvariant() switch
    {
        "safety" => """
            After your analysis, finish your response with this FORM_FIELDS section.
            Fill each value from the incident. Use only the choices shown; write Unknown if unsure.

            FORM_FIELDS:
            incident_type: Workplace Accident
            incident_date:
            location:
            description:
            person_role: Unknown
            injury_type: Unknown
            body_part:
            first_aid_given: Unknown
            riddor_reportable: Unknown
            riddor_category: Unknown
            area_closed: Unknown
            """,

        "security" => """
            After your analysis, finish your response with this FORM_FIELDS section.
            Fill each value from the incident. Use only the choices shown; write Unknown if unsure.

            FORM_FIELDS:
            incident_type: Unauthorised Access
            location:
            description:
            cctv_status: Unknown
            footage_preserved: Unknown
            police_notified: Unknown
            threat_level: Medium
            area_locked_down: Unknown
            """,

        "facilities" => """
            After your analysis, finish your response with this FORM_FIELDS section.
            Fill each value from the incident. Use only the choices shown; write Unknown if unsure.

            FORM_FIELDS:
            asset_type: Structural
            location:
            description:
            severity: Medium
            area_operational: Unknown
            contractor_required: Unknown
            estimated_restoration:
            """,

        _ => ""
    };

    private static string BuildSynthesisPrompt(
        string originalPrompt,
        Dictionary<string, string> results,
        List<string> selected)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Synthesise the following specialist assessments into a unified incident response.");
        sb.AppendLine();
        sb.AppendLine("ORIGINAL INCIDENT:");
        sb.AppendLine(originalPrompt);
        sb.AppendLine();

        foreach (var (domain, finding) in results)
        {
            sb.AppendLine($"{domain.ToUpperInvariant()} SPECIALIST ASSESSMENT:");
            sb.AppendLine(finding);
            sb.AppendLine();
        }

        var skipped = new[] { "safety", "security", "facilities" }
            .Except(selected, StringComparer.OrdinalIgnoreCase).ToList();
        if (skipped.Count > 0)
            sb.AppendLine($"NOTE: {string.Join(", ", skipped).ToUpperInvariant()} domains are not relevant to this incident.");

        sb.AppendLine();
        sb.AppendLine("Respond in EXACTLY this format (all fields required, no extra text):");
        sb.AppendLine("EXECUTIVE_SUMMARY: <2-3 sentences on what happened and the key risks>");
        sb.AppendLine("SEVERITY: <Low or Medium or High or Critical>");
        sb.AppendLine("IMMEDIATE_ACTIONS:");
        sb.AppendLine("- <action 1>");
        sb.AppendLine("- <action 2>");
        sb.AppendLine("24H_ACTIONS:");
        sb.AppendLine("- <action 1>");
        sb.AppendLine("- <action 2>");
        sb.AppendLine("REGULATORY: <RIDDOR/HSE requirements, or None identified>");

        return sb.ToString();
    }

    // ── Result model ──────────────────────────────────────────────────────────

    private sealed class ClassificationResult
    {
        public bool                       NeedsClarification    { get; init; }
        public string?                    ClarificationQuestion { get; init; }
        public string?                    Reasoning             { get; set; }
        public List<string>               SelectedAgents        { get; } = new();
        public Dictionary<string, string> TailoredQuestions     { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
