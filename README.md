# Microsoft AI Agent Orchestration Patterns — POC

A runnable proof-of-concept demonstrating all five [Microsoft AI Agent Orchestration Patterns](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns), plus a **recommended Hybrid pattern** that combines them for production use.

Built with **.NET 10**, **Semantic Kernel 1.76**, and **Llama 3.2 running locally via Ollama** — no cloud account, no API key, no cost.

The domain throughout is workplace health, safety, and facilities management — a real-world multi-domain scenario that naturally exercises each pattern's strengths and weaknesses differently.

![.NET 10](https://img.shields.io/badge/.NET-10.0-blue)
![Semantic Kernel](https://img.shields.io/badge/Semantic%20Kernel-1.76-purple)
![License: MIT](https://img.shields.io/badge/License-MIT-green)
![Ollama](https://img.shields.io/badge/LLM-Ollama%20%2B%20Llama%203.2-orange)

---

## What this demonstrates

| Pattern | What it shows | Demo scenario |
|---|---|---|
| 🔗 **Sequential** | Agents chained in order — each builds on the last | Incident documentation pipeline: capture → assess → compliance → formal report |
| ⚡ **Concurrent** | All agents run in parallel on the same input | Multi-domain incident: Safety, Security, Facilities analyse simultaneously |
| 💬 **Group Chat** | Agents collaborate in a shared conversation thread | Enforcement decision council: debate and reach consensus across rounds |
| 🔀 **Handoff** | Dynamic routing — agents pass control to the right specialist | Incident triage: route to the most appropriate specialist based on content |
| 🧠 **Magentic** | Manager agent builds and adapts a task plan dynamically | Complex incident: plan evolves as specialist findings arrive |
| 🎯 **Hybrid** ⭐ | Classify → clarify if needed → selective fan-out → synthesise | The recommended production pattern — see below |

---

## Hybrid pattern in detail

The Hybrid pattern is the main focus of this POC and demonstrates how a real Sainsbury's Assist gateway could work.

```
User Prompt
    │
    ▼
┌─────────────────────────────────────────────────────────┐
│  Coordinator — Phase 1: Routing                         │
│                                                         │
│  • Keyword scope guard (no LLM call for obvious OOS)   │
│  • Up to 3 clarification rounds if input is too vague  │
│  • Selects only relevant specialists (1–3)             │
│  • Crafts a tailored question for each                 │
└──────────────────────┬──────────────────────────────────┘
                       │  selected domains only
                       ▼
        ┌──────────────┼──────────────┐
        │              │              │
   [Safety]      [Security]    [Facilities]    ← run in parallel
        │              │              │
        └──────────────┼──────────────┘
                       │
                       ▼
              FORM_FIELDS parsed
              → FilledForm emitted        ← Forms tab populates
              → FormFilled SSE event
                       │
                       ▼
┌─────────────────────────────────────────────────────────┐
│  Coordinator — Phase 3: Synthesis                       │
│                                                         │
│  • Executive summary                                    │
│  • Severity rating (Low / Medium / High / Critical)    │
│  • Immediate actions + 24-hour actions                 │
│  • Regulatory obligations (RIDDOR / HSE)               │
└─────────────────────────────────────────────────────────┘
```

### Incident report forms

Each specialist appends a structured `FORM_FIELDS:` block to its response. The backend parses this into a `FilledForm` model — a multi-page form pre-populated with AI-extracted values — which is emitted as a `FormFilled` SSE event and rendered in the **Forms** tab.

Three form schemas are built in:

| Domain | Pages | Fields |
|---|---|---|
| Safety | Incident Overview · Injury & Personnel · Regulatory & Closure | 11 |
| Security | Incident Details · CCTV & Evidence · Threat Assessment | 8 |
| Facilities | Asset Details · Damage Assessment · Restoration Plan | 7 |

AI-filled fields are highlighted so users can review and correct them before submitting. The submit action generates a mock incident reference (e.g. `INC-2025-0042`).

### Dormant MCP client

The codebase includes a scaffolded MCP (Model Context Protocol) layer ready to be activated when a local MCP server is available:

```
IMcpFormClient
    ├── DormantMcpFormClient  ← registered by default, returns null → falls back to text extraction
    └── LiveMcpFormClient     ← implement this when MCP server is ready
```

`McpToolSchemas.cs` contains the full tool specification (all three domains) that the future MCP server must implement — it acts as the living contract between the backend and the server.

To activate: implement `LiveMcpFormClient`, swap one DI line in `ServiceCollectionExtensions.cs`, and set `Mcp:Endpoint` in `appsettings.json`.

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | 10.0+ | `dotnet --version` to check |
| [Ollama](https://ollama.com/download) | Any recent | Desktop app — installs like Chrome |
| Llama 3.2 model | 3B (default) | Pulled via `ollama pull llama3.2` |
| RAM | 8 GB minimum | 16 GB recommended |
| Disk space | ~3 GB free | For the model file |

---

## Quick Start

### 1 — Install Ollama

Download from [https://ollama.com/download](https://ollama.com/download) for your platform. Ollama installs as a background service, starts automatically, and exposes a local API at `http://localhost:11434` — no account or API key needed.

### 2 — Pull Llama 3.2

```bash
ollama pull llama3.2
```

This downloads the model (~2 GB) once and caches it locally. All inference runs on your machine — nothing leaves your network.

Verify:

```bash
ollama list
# Should show: llama3.2   2.0 GB   ...
```

### 3 — Clone and run

```bash
git clone https://github.com/sainsburys-tech/Microsft-Agentic-Kernel.git
cd Microsft-Agentic-Kernel/src/Web
dotnet run
```

Then open **http://localhost:5225** in your browser.

> **First run will be slow** — .NET needs to restore packages and build. Subsequent runs are fast.

---

## Using the app

### Conversation tab

The main chat view. For the Hybrid pattern this includes:

- **Agent progress panel** — a live card showing each specialist's status (spinning while thinking, checkmark when complete) instead of exposing raw streaming output
- **Clarification prompt** — if the input is too vague the Coordinator asks a follow-up question directly in the chat thread before routing
- **Structured summary card** — colour-coded severity badge, executive summary, two-column actions grid (immediate vs. 24-hour), and a regulatory callout when RIDDOR/HSE obligations apply

```
┌─────────────────────────────────────────────────────────────┐
│  💬 Conversation  │  📋 Forms                               │
├───────────────────┴─────────────────────────────────────────┤
│                                                             │
│  ▶ Coordinator analysing prompt...                          │
│                                                             │
│  ┌─ Specialists handling this incident ──────────────────┐  │
│  │  🔴 Safety Specialist        ●●● working...           │  │
│  │  🔵 Security Specialist      ✓ done                   │  │
│  │  🟢 Facilities Specialist    ✓ done                   │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌─ HIGH  Incident Summary ──────────────────────────────┐  │
│  │  Executive summary...                                  │  │
│  │  ┌──────────────────┬───────────────────────────────┐ │  │
│  │  │ Immediate Actions│ 24-Hour Actions                │ │  │
│  │  │ • ...            │ • ...                          │ │  │
│  │  └──────────────────┴───────────────────────────────┘ │  │
│  │  ⚠ RIDDOR: Specified Injury — report within 10 days   │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

### Forms tab

Unlocks once the specialists have responded. Shows each domain's pre-filled incident report form with:

- Domain tabs (Safety / Security / Facilities) when multiple are relevant
- Multi-page navigation with breadcrumb
- AI-filled fields highlighted in amber — review and correct before submitting
- Submit generates a mock incident reference number

---

## Architecture

```
src/
├── Core/
│   ├── Interfaces/
│   │   └── IMcpFormClient.cs          # MCP abstraction
│   ├── Models/
│   │   ├── AgentEvent.cs              # SSE event record + factory methods
│   │   └── FormModels.cs              # FilledForm, FilledFormPage, FilledFormField,
│   │                                  # StructuredSummaryData
│   ├── Infrastructure/
│   │   ├── AssistAgent.cs             # Base class — streaming + non-streaming invoke
│   │   ├── ConversationStore.cs       # Pause/resume SSE stream for clarification
│   │   ├── FormSchemaRegistry.cs      # 3 domain schemas + FORM_FIELDS parser
│   │   ├── KernelFactory.cs           # Ollama → Semantic Kernel setup
│   │   └── Mcp/
│   │       ├── McpFormClientModels.cs # McpToolDefinition, McpToolParameter records
│   │       ├── McpToolSchemas.cs      # Tool spec for all 3 domains (future server contract)
│   │       └── DormantMcpFormClient.cs# No-op — returns null, logs, falls back to text extraction
│   └── Extensions/
│       └── ServiceCollectionExtensions.cs
│
├── Agents/
│   ├── CoordinatorAgent.cs            # Routing + synthesis (two responsibilities, one agent)
│   ├── SafetySpecialistAgent.cs       # RIDDOR, HSE, first aid, risk assessment
│   ├── SecuritySpecialistAgent.cs     # CCTV, access control, threat assessment
│   └── FacilitiesSpecialistAgent.cs  # Building, utilities, equipment, restoration
│
├── Patterns/
│   ├── Sequential/SequentialPatternRunner.cs
│   ├── Concurrent/ConcurrentPatternRunner.cs
│   ├── GroupChat/GroupChatPatternRunner.cs
│   ├── Handoff/HandoffPatternRunner.cs
│   ├── Magentic/MagenticPatternRunner.cs
│   ├── Hybrid/HybridPatternRunner.cs  # Main pattern — see diagram above
│   └── PatternRegistry.cs
│
└── Web/
    ├── Program.cs                     # Minimal API endpoints
    └── wwwroot/index.html             # Single-page UI (vanilla JS, no framework)
```

### API endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/patterns` | Returns all pattern metadata (name, description, pros/cons, default prompt) |
| `GET` | `/api/run` | SSE stream — runs a pattern and streams `AgentEvent` objects as JSON |
| `POST` | `/api/respond` | Sends a user's clarification reply back into a paused SSE stream |

### SSE event types

| Event type | When emitted | Contains |
|---|---|---|
| `SystemNote` | Phase transitions, routing decisions | Status message string |
| `AgentThinking` | Agent about to respond | Agent name + colour |
| `AgentResponse` | Token-by-token streaming | Content chunk |
| `Reasoning` | After routing decision | Coordinator's classification reasoning |
| `ClarificationRequest` | Input too vague to route | Question + `conversationId` for reply |
| `FormFilled` | After each specialist completes | Full `FilledForm` model |
| `StructuredSummary` | End of Phase 3 | `StructuredSummaryData` (severity, actions, etc.) |
| `Complete` | Run finished | Summary of what happened |
| `Error` | Something failed | Error message |

### How the clarification pause works

```
Browser ──── GET /api/run?pattern=hybrid&prompt=... ────► SSE stream starts
                                                               │
                                              ClarificationRequest emitted
                                              (stream pauses on TCS)
                                                               │
Browser ──── POST /api/respond { conversationId, reply } ─────► TCS resolved
                                                               │
                                              SSE stream resumes
                                              (Coordinator re-classifies with full Q&A)
```

---

## Configuration

`src/Web/appsettings.json`:

```json
{
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "ModelId": "llama3.2"
  }
}
```

### Switching models

Larger models give better reasoning quality — particularly for the structured output the Hybrid pattern expects:

```bash
# Fast (default) — good for demos, occasional format drift
ollama pull llama3.2        # 3B params, ~2 GB

# Better quality — recommended for reliable form extraction
ollama pull llama3.1        # 8B params, ~5 GB

# Best quality — requires 16 GB+ RAM
ollama pull llama3.3        # 70B params, ~40 GB
```

Update `ModelId` in `appsettings.json` to match.

> **Note on small models:** Llama 3.2 3B occasionally drifts from the expected structured output format (e.g. `Form Fields:` instead of `FORM_FIELDS:`). The parser accepts multiple capitalisation variants to handle this. Larger models (8B+) are significantly more reliable.

### Running on a remote Ollama instance

```json
{
  "Ollama": {
    "BaseUrl": "http://192.168.1.100:11434",
    "ModelId": "llama3.2"
  }
}
```

---

## Pattern guide

### 🔗 Sequential — Pipeline / Prompt Chaining

Each agent receives the **complete output of the previous agent** as its input. The pipeline is predefined and linear.

```
Input → Capture → Safety → Compliance → Report Generation → Result
```

**Use when:** You have a clear multi-stage workflow where each step depends on the previous.

**Avoid when:** Stages are independent — they'd be faster running in parallel.

---

### ⚡ Concurrent — Fan-out / Fan-in

All agents receive the **same input simultaneously** and work independently. Results are aggregated by a Coordinator.

```
             ┌── Safety ────┐
Input ───────├── Security ──┤──── Coordinator ──── Result
             └── Facilities ┘
```

**Use when:** You need multiple independent perspectives and speed matters.

**Avoid when:** Agents need to build on each other's outputs.

> **Note:** Concurrent runs all agents regardless of relevance — a wet floor incident still invokes the Security specialist even though it has nothing to contribute. The Hybrid pattern fixes this.

---

### 💬 Group Chat — Roundtable / Maker-Checker

Agents share a **single conversation thread** and take turns. Each agent reads all prior turns before contributing.

**Use when:** Decisions benefit from deliberation — agents challenging and building on each other's reasoning.

**Avoid when:** Real-time speed is required or you have more than 3–4 agents.

---

### 🔀 Handoff — Triage / Dynamic Delegation

**One agent is active at a time.** Each agent handles its domain then decides whether to hand off to the next specialist.

**Use when:** The right specialist isn't known upfront and the problem type emerges during processing.

**Avoid when:** You need concurrent analysis or routing loops are a risk (a cap of 5 hops is enforced).

---

### 🧠 Magentic — Adaptive Planning / Task Ledger

A **Manager Agent builds a task ledger** — a dynamic plan of investigation tasks. The plan is reviewed and refined as specialist findings arrive. Tasks can be added, removed, or reordered mid-flight.

**Use when:** The problem is open-ended and the solution path isn't known in advance.

**Avoid when:** Speed is critical or the workflow is well-defined. This is the most expensive pattern.

---

### 🎯 Hybrid — Recommended Production Pattern

See the [detailed breakdown](#hybrid-pattern-in-detail) above.

**Use when:** You want the intelligence of Magentic with the speed of Concurrent, plus structured output and form pre-population for downstream workflows.

---

## Troubleshooting

### "Could not load patterns" in the browser

The ASP.NET Core server isn't running. Run `dotnet run` from `src/Web/` and refresh.

### Forms tab is greyed out

The Forms tab unlocks once at least one specialist has successfully parsed its `FORM_FIELDS` block. If it stays greyed out after the run completes, the model didn't follow the structured output format. Try a more descriptive prompt, or switch to a larger model (8B+) for more reliable formatting.

### Blank agent stream after clicking Run Demo

Hard refresh the browser (`Ctrl+Shift+R`) to clear cached JavaScript, then try again.

### Coordinator keeps asking clarifying questions

The input may be genuinely vague. Add detail about location, what happened, and who was involved. The Coordinator will proceed after 3 rounds even without a complete picture.

### Very slow responses

- Llama 3.2 (3B) runs on CPU if you have no supported GPU. Expect 5–20 seconds per agent on CPU-only machines.
- Check Ollama is running: `ollama list` should show your model.
- If Ollama isn't running, start it: `ollama serve`.

### Port already in use

Another instance of the app is running. Kill it, or change the port in `src/Web/Properties/launchSettings.json`.

### Model not found

Run `ollama pull llama3.2` and ensure `ModelId` in `appsettings.json` matches exactly.

---

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md).

## Code of Conduct

See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Security

See [SECURITY.md](SECURITY.md).

## Licence

MIT — see [LICENSE](LICENSE).
