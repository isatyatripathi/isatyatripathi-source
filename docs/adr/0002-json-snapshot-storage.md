# ADR 0002: Use in-memory state with JSON snapshots

**Status:** Accepted

## Context

A pure in-memory store matches the requested simplicity but would erase daily collection, review edits, and source configuration on every restart.

## Decision

Keep runtime state in memory and persist atomic, versioned JSON snapshots by default. Provide a `MemoryOnly` mode and hide both behind repository/workspace interfaces.

## Consequences

- No database server or migration tool is required.
- Human work survives restarts.
- Full-file writes limit practical scale; this is acceptable for the MVP.
- A later SQLite/PostgreSQL adapter can replace the implementation without changing use cases.
