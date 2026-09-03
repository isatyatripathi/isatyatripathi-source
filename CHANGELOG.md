# Changelog

## 0.1.0 — 2026-08-31

Initial backend MVP for DevSignal Studio.

### Added

- .NET 10 modular-monolith solution with Domain, Application, Infrastructure, API, and smoke-test projects.
- Local JSON snapshot workspace with in-memory operation and atomic rotating backups.
- RSS/Atom, Stack Exchange, local JSON, remote JSON, and manual source connectors.
- URL normalization, fingerprint deduplication, weighted relevance scoring, candidate selection, and ingestion history.
- Deterministic mock, Ollama, OpenAI Chat Completions-compatible, Anthropic Messages, and generic OpenAI-compatible AI adapters.
- Task-level provider routes with ordered fallbacks and environment-variable credential references.
- LinkedIn and Medium draft recipes, source-linked claims, Mermaid sanitization, revision history, validation, approval, rejection, export, and manual publication recording.
- Bounded background queues, cancellation, and a configurable daily local scheduler.
- Minimal API endpoints, local Vite CORS policy, readiness/liveness checks, and problem responses.
- Configurable topic taxonomy covering .NET, React, AI, MCP, system design, cloud, DevOps, performance, security, testing, interviews, leadership, and career growth.
- Offline request collection, PowerShell and shell scripts, dependency-free repository verifier, and 12 runtime smoke checks.

### Phase boundary

The React + Vite + TypeScript review workspace is planned for the next implementation phase. Automatic social publishing remains intentionally excluded.
