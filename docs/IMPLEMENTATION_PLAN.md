# Implementation Plan

The order intentionally follows **architecture → backend → frontend**.

## Phase 0 — Architecture and contracts

Deliverables in this pack:

- module boundaries and dependency rules;
- domain model and state machine;
- source/provider/storage extension contracts;
- API contract;
- configuration schemas and samples;
- security and prompt-injection controls;
- acceptance criteria and ADRs.

Exit gate: no unresolved architectural blocker for the MVP loop.

## Phase 1 — Backend foundation

1. Create the .NET 10 solution and projects.
2. Add domain entities, value objects, result/error model, and state transitions.
3. Add application use cases and interfaces.
4. Implement in-memory repositories plus atomic JSON snapshots.
5. Load and validate topics, recipes, sources, and provider configuration.
6. Implement local JSON and manual content connectors.
7. Implement URL canonicalization, content fingerprints, deduplication, and deterministic scoring.
8. Add API endpoints for dashboard, sources, items, and settings.
9. Add health checks, structured logs, cancellation, and problem details.
10. Add unit, contract, integration, and architecture tests.

Exit gate: custom JSON can be ingested, scored, queried, restarted, and recovered from snapshots.

## Phase 2 — Backend ingestion and AI

1. Add RSS/Atom connector.
2. Add Stack Exchange connector.
3. Add daily `BackgroundService` and run/cancellation tracking.
4. Add mock AI provider and structured-output parser.
5. Add Ollama provider.
6. Add OpenAI and Anthropic providers behind the same contract.
7. Add task routing, fallback, timeout, retry, and usage metadata.
8. Add recipe/prompt loader, draft generation, claim ledger, and revisions.
9. Add Mermaid sanitizer and validation contract.
10. Add export endpoints.

Exit gate: one command ingests approved sources and creates reviewable, attributed drafts without external AI credentials; configured providers can replace the mock.

## Phase 3 — React frontend

1. Scaffold React + Vite + TypeScript.
2. Add typed API client, query state, routing, and error boundaries.
3. Build dashboard and “Run now” flow.
4. Build source management and connector test UI.
5. Build discover queue with filters and score explanations.
6. Build draft editor with revision awareness.
7. Add Mermaid strict rendering, source editor, and SVG/PNG export.
8. Add provider/settings screens with secret-safe forms.
9. Add activity/run views.
10. Add component and end-to-end tests.

Exit gate: the complete daily loop works from the browser.

## Phase 4 — Hardening

- feed/API fixtures and regression suite;
- CSP and SSRF penetration tests;
- prompt-injection adversarial tests;
- schema migrations and backup recovery;
- observability dashboard;
- content quality evaluations;
- packaging scripts for Windows, macOS, and Linux.

## Deferred enhancements

- SQLite or PostgreSQL persistence;
- embeddings and semantic duplicate detection;
- trend clustering and knowledge graph;
- content calendar and series planning;
- analytics import and feedback-based topic weighting;
- browser extension or bookmarklet for manual capture;
- MCP server exposing approved read-only resources and tools;
- optional authorized publishing adapters;
- cloud or multi-user deployment.
