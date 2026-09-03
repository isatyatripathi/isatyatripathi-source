# Backend implementation — MVP 0.1

## Status

The ASP.NET Core backend is implemented as a local-first modular monolith targeting `net10.0`. The React frontend is intentionally not part of this phase; the API contract and CORS policy are ready for a Vite development server on port `5173`.

The implementation uses only the .NET shared framework. There are no external NuGet package references, database servers, message brokers, or hosted dependencies required for the default local workflow.

## Solution layout

| Project | Responsibility |
|---|---|
| `DevSignalStudio.Domain` | Records, enums, validation reports, draft lifecycle rules, run models, configuration models |
| `DevSignalStudio.Application` | Content normalization, relevance scoring, ingestion orchestration, drafting, review workflow, dashboard queries |
| `DevSignalStudio.Infrastructure` | JSON configuration and snapshots, connectors, AI adapters, HTTP safety, Mermaid sanitization, queues and hosted workers |
| `DevSignalStudio.Api` | Minimal API endpoints, problem responses, CORS, startup initialization and health checks |
| `DevSignalStudio.Tests` | Dependency-free console smoke tests covering the most important backend workflows |

The projects point inward: Infrastructure and API depend on Application and Domain; Domain does not depend on framework-specific infrastructure.

## Implemented capabilities

### Content collection

The connector abstraction supports:

- RSS and Atom feeds
- Stack Exchange API responses
- Local JSON files
- Remote JSON endpoints
- Explicit manual capture

Each source is configured in `config/sources.json` with an ID, connector type, trust weight, tags, polling interval, item limit and compliance notes.

Manual sources are never polled. They are populated through `POST /api/v1/items/manual`, which prevents a scheduled run from creating predictable warning noise.

### Normalization and deduplication

Collected items are normalized before persistence:

- HTML entities and whitespace are normalized.
- URLs are canonicalized.
- Common tracking parameters are removed.
- A SHA-256 content fingerprint is produced.
- Canonical URLs and fingerprints are used for duplicate detection.
- Source provenance is retained with every item.

### Relevance scoring

The deterministic scorer executes before any AI call. It combines:

- weighted topic matches
- freshness
- configured source authority
- learning value
- career alignment
- novelty compared with existing content
- discussion potential
- hype-language penalties

The taxonomy is fully editable in `config/topics.json`. Candidate and draft limits are controlled by the taxonomy profile.

### Ingestion orchestration

An ingestion run can be started manually or by the local scheduler. A run:

1. Selects enabled, pollable sources.
2. Honors per-source polling intervals unless `force=true`.
3. Fetches a bounded number of items.
4. Normalizes and deduplicates them.
5. Scores them against the current taxonomy.
6. Selects the strongest daily candidates.
7. Persists items and run summaries.
8. Optionally queues draft-generation runs.

Each source failure is isolated. A partially successful run completes with warnings rather than losing useful results from other sources.

### AI routing

`IAiRouter` selects providers through task-level routes in `config/ai-providers.json`. Each route contains ordered fallback lists for:

- classification
- drafting
- diagram generation

The implemented adapters are:

- deterministic mock provider
- Ollama
- OpenAI Chat Completions-compatible API
- Anthropic Messages API
- generic OpenAI-compatible API

The default `offline` route uses the deterministic mock provider, so the complete ingestion-to-draft workflow can be exercised without a model, network access or API key.

Provider credentials are referenced by environment-variable name. Secret values are not stored in JSON configuration.

### Draft generation and review

Draft generation:

- accepts one to ten source items
- selects an enabled content recipe
- treats source text as untrusted reference material
- asks for structured JSON output
- validates returned source IDs
- builds a claim ledger
- sanitizes Mermaid text
- records provider, model, duration, token usage and fallback errors
- creates revision 1 in `inReview` status

The review workflow supports:

- editing with optimistic revision checks
- validation
- approval only from `inReview`
- rejection only from `inReview`
- export as plain text, Markdown, JSON or Mermaid
- recording manual publication only after approval

Publication URLs must be absolute HTTP or HTTPS URLs without embedded credentials. The application never posts automatically to LinkedIn or Medium.

### Local persistence

Runtime state is held in in-memory dictionaries. In `JsonSnapshot` mode it is persisted to versioned JSON snapshots under `data/`:

- `items.json`
- `drafts.json`
- `ingestion-runs.json`
- `generation-runs.json`

Writes are atomic and can keep rotating backups. `MemoryOnly` mode skips persistence. The configured storage directory is required to remain inside the repository root.

### Background processing

Bounded in-process channels decouple API requests from long-running ingestion and generation work. Hosted workers execute queued run IDs and maintain cancellation tokens.

The daily scheduler reads `config/profile.json`, evaluates the configured local time, respects `maximumRunsPerDay`, and can queue an overdue run after application startup.

### Security controls

The first backend phase includes:

- outbound URL scheme validation
- blocking embedded URL credentials
- blocking private, link-local, multicast and special-use network ranges
- explicit loopback access only for local AI providers
- redirect revalidation and redirect limits
- response-size limits
- request timeouts and cancellation
- no automatic redirect following
- Mermaid directive, link, click-handler and HTML blocking
- bounded queues and bounded source item counts
- configuration validation and path containment checks
- structured problem responses without exposing internal 500-level details

A remaining network-level limitation is DNS rebinding between pre-resolution and the underlying HTTP connection. The local-first threat model and manual configuration boundary reduce the exposure, but a production-hosted version should pin validated addresses through a custom connection callback.

## API groups

| Resource | Base path |
|---|---|
| Dashboard | `/api/v1/dashboard` |
| Sources | `/api/v1/sources` |
| Ingestion | `/api/v1/ingestion/runs` |
| Content items | `/api/v1/items` |
| Drafts | `/api/v1/drafts` |
| Draft-generation runs | `/api/v1/draft-generation/runs` |
| Topics | `/api/v1/topics` |
| Recipes | `/api/v1/recipes` |
| Providers and routes | `/api/v1/providers` |
| Profile, schedule and storage | `/api/v1/settings` |
| Combined run history | `/api/v1/runs` |
| Health | `/health/live`, `/health/ready` |

The complete interactive request collection is in `requests/DevSignalStudio.Api.http`.

## Extension points

The current design is intended to grow without splitting the application prematurely.

To add a source connector:

1. Implement `IContentConnector`.
2. Register it in `ServiceCollectionExtensions`.
3. Add source definitions using the new connector type.

To add an AI provider:

1. Implement `IAiProviderAdapter`.
2. Give it a unique `ProviderType`.
3. Register the adapter.
4. Add a provider and include its ID in one or more task routes.

To replace JSON persistence:

1. Implement `IContentWorkspace`.
2. Replace the dependency-injection registration.
3. Keep Application workflows unchanged.

To add a new content format:

1. Add a recipe to `config/content-recipes.json`.
2. Add recipe-specific instructions when needed.
3. Reuse the same generation and review workflow.

## Deliberately deferred

The backend MVP does not yet include:

- the React/Vite user interface
- browser-side Mermaid SVG/PNG export
- AI-assisted relevance classification after deterministic scoring
- embeddings or semantic vector search
- automatic LinkedIn or Medium publishing
- generic scraping of LinkedIn or Quora
- authentication or multi-user authorization
- a database-backed workspace
- distributed queues or multiple worker processes
- production-grade DNS pinning

These are explicit phase boundaries rather than hidden placeholders.
