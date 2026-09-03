# ADR 0001: Use a modular monolith

**Status:** Accepted

## Context

The application is local, single-user, and early-stage, but must support future connectors, AI providers, storage engines, and channels.

## Decision

Use one ASP.NET Core process organized into strict domain/application/infrastructure/API modules. Use explicit ports and adapters, module registration, and architecture tests.

## Consequences

- Simple launch, debugging, and local packaging.
- No network boundaries, broker, distributed tracing setup, or eventual-consistency overhead in v1.
- Modules can be extracted later if workload or deployment requirements justify it.
- Boundaries must be enforced in code rather than assumed from separate services.
