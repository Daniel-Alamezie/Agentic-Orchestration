# Contributing

Thank you for your interest in contributing! This is a reference POC — the bar is kept intentionally low so contributions are easy to make.

---

## Ways to contribute

| Type | Examples |
|---|---|
| 🐛 Bug fix | SSE streaming edge case, incorrect agent output, UI glitch |
| ✨ New pattern | Add a Reflection pattern, Critic/Verifier, or Swarm |
| 🤖 New agent | Add a domain specialist (HR, Legal, Finance) |
| 📖 Docs | Improve setup steps, add more troubleshooting, translate |
| 🧪 Tests | Unit tests for pattern runners or agent parsing logic |

---

## Getting started

1. **Fork** the repository and clone your fork
2. Create a branch: `git checkout -b feat/my-improvement`
3. Make your changes (see project structure in README)
4. Run the app locally and verify it works end-to-end:
   ```bash
   cd src/Web
   dotnet run
   # open http://localhost:5225
   ```
5. Open a Pull Request against `main`

---

## Adding a new pattern

1. Create `src/Patterns/YourPattern/YourPatternRunner.cs`
2. Implement `IPatternRunner` (see `src/Core/Interfaces/IPatternRunner.cs`)
3. Register it in `src/Patterns/PatternRegistry.cs`
4. The sidebar auto-populates from the registry — no UI changes needed

---

## Code style

- Follow existing conventions (file-scoped namespaces, primary constructors, C# 12 features)
- Keep agents' system prompts focused on a single domain
- Prefer `IAsyncEnumerable<AgentEvent>` for all streaming paths

---

## Reporting issues

Open a GitHub Issue with:
- What you expected to happen
- What actually happened (error message, screenshot)
- Your OS, .NET version, Ollama version, and model name

---

## Licence

By contributing you agree that your contributions will be licensed under the [MIT Licence](LICENSE).
