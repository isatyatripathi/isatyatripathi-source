# ADR 0003: Route AI tasks through capability-based providers

**Status:** Accepted

## Context

The system must support local and hosted models with different APIs, costs, privacy properties, and capabilities.

## Decision

Define a vendor-neutral `IAiProvider`, capability metadata, structured generation contract, and task-specific fallback routes. Start with mock, Ollama, OpenAI, Anthropic, and OpenAI-compatible adapters.

## Consequences

- Vendor SDK details stay in infrastructure.
- The app can run offline with the mock provider or local Ollama.
- Provider-specific features are available only through declared capabilities.
- Normalization and contract tests are required for each adapter.
