# ADR 0005: Prefer approved feeds, APIs, JSON, and manual capture

**Status:** Accepted

## Context

The desired sources have different access models. Generic scraping is brittle and may violate terms, authentication boundaries, or content rights.

## Decision

Implement RSS/Atom, documented APIs, local/custom JSON, and manual item capture first. HTML extraction is a later connector that must be explicitly enabled per source and comply with access rules.

## Consequences

- Better reliability and provenance.
- Some sources require manual capture rather than automatic collection.
- The connector interface still permits future approved integrations.
