# AI Agent Orchestration Patterns — POC

A runnable proof-of-concept demonstrating all five [Microsoft AI Agent Orchestration Patterns](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns), plus a **recommended hybrid pattern** that combines them.

Built with **.NET 10**, **Semantic Kernel**, and **Llama 3.2 running locally via Ollama** — no cloud account, no API key, no cost.

The domain used throughout is workplace health, safety, and facilities management — a real-world multi-domain scenario that naturally exercises each pattern differently.

![.NET 10](https://img.shields.io/badge/.NET-10.0-blue)
![License: MIT](https://img.shields.io/badge/License-MIT-green)
![Ollama](https://img.shields.io/badge/LLM-Ollama%20%2B%20Llama%203.2-orange)

---

## What this demonstrates

| Pattern | What it shows | Demo scenario |
|---|---|---|
| 🔗 **Sequential** | Agents chained in order — each builds on the last | Incident report pipeline: capture → assess → compliance → formal report |
| ⚡ **Concurrent** | All agents run in parallel on the same input | Multi-domain incident: Safety, Security, Facilities analyse simultaneously |
| 💬 **Group Chat** | Agents collaborate in a shared conversation thread | Enforcement decision council: debate and reach consensus |
| 🔀 **Handoff** | Dynamic routing — agents pass control to the right specialist | Incident triage: route to the most appropriate specialist |
| 🧠 **Magentic** | Manager agent builds and adapts a task plan dynamically | Complex incident: plan evolves as specialist findings arrive |
| 🎯 **Hybrid** ⭐ | Classify → selective concurrent fan-out → synthesise | The recommended production pattern combining the best of all five |

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

Download and run the installer for your platform:

- **Windows / macOS / Linux:** [https://ollama.com/download](https://ollama.com/download)

Ollama installs as a background service and starts automatically. It exposes a local API at `http://localhost:11434` — no account or API key needed.

### 2 — Pull Llama 3.2

Open a terminal and run:

```bash
ollama pull llama3.2
```

This downloads the model (~2 GB) once and caches it locally. All inference runs on your machine — nothing leaves your network.

Verify it worked:

```bash
ollama list
# Should show: llama3.2   2.0 GB   ...
```

### 3 — Clone and run

```bash
git clone https://github.com/your-org/ai-agent-patterns-poc.git
cd ai-agent-patterns-poc/src/Web
dotnet run
```

Then open **http://localhost:5225** in your browser.

> **First run will be slow** — .NET needs to restore packages and build. Subsequent runs are fast.

---

## Using the app

```
┌─────────────────────┬────────────────────────────────────────────┐
│  SIDEBAR            │  MAIN AREA                                 │
│                     │                                            │
│  Orchestration      │  Pattern name + scenario description       │
│  Patterns           │  Quick pros/cons strip                     │
│  ─────────────      │                                            │
│  🔗 Sequential      │  Scenario Prompt (pre-filled, editable)   │
│  ⚡ Concurrent      │  [Run Demo]  [Clear]                       │
│  💬 Group Chat      │                                            │
│  🔀 Handoff         │  Agent Activity (streams in real time)     │
│  🧠 Magentic        │  ┌─────────────────────────────────────┐   │
│                     │  │ 🔴 Safety Specialist                │   │
│  ─────────────      │  │    ● ● ●  thinking...              │   │
│  ⭐ Hybrid          │  │    [response builds token by token] │   │
│  (Recommended)      │  └─────────────────────────────────────┘   │
└─────────────────────┴────────────────────────────────────────────┘
```

1. Click a pattern in the sidebar
2. Read the scenario description and pros/cons (click **Pros & Cons** for full detail)
3. Edit the prompt if you want (or use the pre-filled example)
4. Click **Run Demo** — agent activity streams in real time, token by token
5. Click **Clear** and switch to the next pattern to compare

> **Tip:** Start with 🎯 **Hybrid (Recommended)** — it shows the full intelligent routing flow. Then try ⚡ **Concurrent** to see three agents running in parallel.

---

## Architecture

```
ai-agent-patterns-poc/
├── src/
│   ├── Core/        # IPatternRunner interface, AgentEvent model,
│   │                # AssistAgent base class, KernelFactory (Ollama setup)
│   │
│   ├── Agents/      # Specialist agents with domain-focused system prompts:
│   │                # SafetySpecialist, SecuritySpecialist,
│   │                # FacilitiesSpecialist, Coordinator
│   │
│   ├── Patterns/    # One orchestrator per pattern:
│   │                # Sequential, Concurrent, GroupChat,
│   │                # Handoff, Magentic, Hybrid
│   │
│   └── Web/         # ASP.NET Core Minimal API
│                    # GET /api/patterns  — pattern metadata
│                    # GET /api/run       — SSE streaming endpoint
│                    # wwwroot/index.html — single-page UI
```

### How streaming works

```
Browser ──── GET /api/run?pattern=hybrid&prompt=... ────► ASP.NET Core
                                                               │
                                                    IPatternRunner.RunAsync()
                                                               │
                                              IAsyncEnumerable<AgentEvent>
                                                               │
                                               ┌──────────────┴──────────────┐
                                               │  Agents call Ollama via     │
                                               │  SK's OpenAI-compat client  │
                                               │  http://localhost:11434/v1  │
                                               └──────────────┬──────────────┘
                                                               │
Browser ◄─── SSE: data: {"agentName":"Safety",...} ───────────┘
         (one event per token, rendered as chat bubbles)
```

**Key design choices:**
- Ollama is connected via Semantic Kernel's OpenAI connector pointed at `localhost:11434/v1` — Ollama speaks the OpenAI API format, so no special adapter is needed
- `IAsyncEnumerable<AgentEvent>` threads streaming from the LLM all the way to the browser with no buffering
- `System.Threading.Channels` merges multiple concurrent agent streams (Concurrent + Hybrid patterns)

---

## Configuration

Edit `src/Web/appsettings.json`:

```json
{
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "ModelId": "llama3.2"
  }
}
```

### Switching models

Any model available in Ollama works. Larger models give better reasoning but are slower:

```bash
# Fast (default) — good for demos
ollama pull llama3.2        # 3B params, ~2 GB

# Better quality
ollama pull llama3.1        # 8B params, ~5 GB

# Best quality (requires 16 GB+ RAM)
ollama pull llama3.3        # 70B params, ~40 GB
```

Then update `ModelId` in `appsettings.json` to match.

### Running on a remote Ollama instance

If Ollama is running on another machine:

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
Input → Agent 1 → Agent 2 → Agent 3 → Agent N → Result
```

**Use when:** You have a clear multi-stage workflow where each stage depends on the previous (e.g. extract → validate → enrich → generate).

**Avoid when:** Stages are independent and could run in parallel.

---

### ⚡ Concurrent — Fan-out / Fan-in

All agents receive the **same input simultaneously** and work independently. Results are aggregated by a coordinator.

```
             ┌── Agent A ──┐
Input → init ├── Agent B ──┤ → Coordinator → Result
             └── Agent C ──┘
```

**Use when:** You need multiple independent perspectives on the same input and speed matters.

**Avoid when:** Agents need to build on each other's outputs.

---

### 💬 Group Chat — Roundtable / Maker-Checker

Agents share a **single conversation thread** and take turns responding. Each agent reads what the others have said before contributing.

**Use when:** Decisions benefit from deliberation and agents challenging each other's reasoning.

**Avoid when:** Real-time speed is required or you have more than 3–4 agents (conversation control degrades).

---

### 🔀 Handoff — Triage / Dynamic Delegation

**One agent is active at a time.** Each agent decides whether to handle the task or transfer it to a more appropriate specialist.

**Use when:** The right specialist isn't known upfront and emerges during processing.

**Avoid when:** You need agents to work simultaneously or routing loops are a risk.

---

### 🧠 Magentic — Adaptive Planning / Task Ledger

A **Manager Agent builds a task ledger** — a dynamic plan of approach. The plan is refined as specialist agents report their findings. Tasks can be added, removed, or reordered.

**Use when:** The problem is open-ended and the solution path isn't known in advance.

**Avoid when:** Speed is critical or the workflow is well-defined. This is the most expensive pattern.

---

### 🎯 Hybrid — Recommended Production Pattern

Combines the best of the above into a single cohesive flow:

```
User Prompt
    │
    ▼
Gateway Classifier          ← Decides which specialists are needed
    │                          and crafts a tailored question for each
    │
    ▼ (only selected agents)
Concurrent Fan-out          ← Selected specialists run in parallel
    │
    ▼
Coordinator Synthesis       ← One unified, prioritised response
```

**Why not just run all agents every time?** A query about a wet floor doesn't need a Security or Facilities specialist. The classifier skips them, making a faster and more focused response. Multi-domain incidents invoke all three in parallel. The system scales intelligently.

---

## Troubleshooting

### "Could not load patterns" in the browser

The ASP.NET Core server isn't running. Run `dotnet run` from `src/Web/` and refresh.

### Blank agent stream after clicking Run Demo

Hard refresh the browser (`Ctrl+Shift+R`) to clear any cached JavaScript, then try again.

### Very slow responses

- Llama 3.2 (3B) runs on CPU if you have no supported GPU. Expect 5–20 seconds per agent on CPU-only machines.
- Check Ollama is running: `ollama list` should show your model.
- If Ollama isn't running, start it with `ollama serve`.

### Port already in use

Another instance of the app is running. Kill it or change the port in `src/Web/Properties/launchSettings.json`.

### Model not found

Run `ollama pull llama3.2` and ensure the `ModelId` in `appsettings.json` matches exactly.

---

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

MIT — see [LICENSE](LICENSE).
