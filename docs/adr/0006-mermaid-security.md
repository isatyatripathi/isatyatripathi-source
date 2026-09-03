# ADR 0006: Store Mermaid text and render with strict security

**Status:** Accepted

## Context

Mermaid is ideal for editable diagrams, but generated syntax and labels are untrusted input rendered in a browser.

## Decision

Store Mermaid source as text, sanitize it on the backend, render with strict security on the frontend, block unsafe directives and external resources, and keep diagram validation separate from draft validity.

## Consequences

- Diagrams remain editable and versionable.
- Sanitization and rendering tests are mandatory.
- Some advanced Mermaid features are intentionally unavailable.
