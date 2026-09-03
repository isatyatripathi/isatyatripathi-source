# DevSignal Studio

A local-first content intelligence and drafting workspace for building a visible professional profile around **full-stack, AI-powered .NET engineering**.

DevSignal Studio collects configurable technology signals, ranks them against a weighted career-focused taxonomy, generates structured LinkedIn or Medium drafts with Mermaid diagrams, and keeps every draft in a human review workflow. It does not publish automatically.

## Delivery status

| Phase | Status |
|---|---|
| Architecture and decision records | Complete |
| ASP.NET Core 10 backend MVP | Implemented |
| React + Vite + TypeScript frontend | Next phase |

The backend can already run as a local API and complete a local-only ingestion-to-draft workflow with the built-in deterministic mock AI provider.

## Technology

- .NET 10 / ASP.NET Core Minimal API
- C# 14
- In-memory runtime state with atomic JSON snapshots
- Hosted background workers and bounded channels
- RSS/Atom, Stack Exchange, JSON and manual source connectors
- Ollama, OpenAI, Anthropic and OpenAI-compatible provider adapters
- Mermaid text generation and security validation
- React + Vite + TypeScript frontend contract prepared through CORS and `/api/v1`

No database server, message broker or external NuGet package is required for the backend MVP.

## What is implemented

- Daily local ingestion scheduler
- Source enable/disable, testing and configuration
- Per-source trust weights, poll intervals and item limits
- URL canonicalization and content-fingerprint deduplication
- Weighted deterministic relevance scoring
- Candidate selection and run history
- Configurable AI provider routes with ordered fallbacks
- Offline deterministic mock provider
- Structured draft generation with claim/source mapping
- Mermaid sanitization
- Draft revisions and optimistic concurrency
- Validate, approve, reject, export and mark-published workflow
- In-memory or JSON snapshot storage
- Dashboard and health endpoints
- Dependency-free static verifier and console smoke tests

## Repository map

```text
DevSignalStudio.sln
config/                         Editable sources, topics, recipes, profile and AI routes
data/                           Runtime JSON snapshots; generated locally
requests/                       HTTP request collection for the full API workflow
scripts/                        Run, test and verification scripts
src/backend/
  DevSignalStudio.Domain/       Core records and lifecycle rules
  DevSignalStudio.Application/  Use cases and orchestration
  DevSignalStudio.Infrastructure/ Connectors, storage, AI, workers and security
  DevSignalStudio.Api/          ASP.NET Core HTTP API
src/frontend/                   Reserved for the React phase
tests/backend/                  Dependency-free backend smoke-test executable
docs/                           Architecture, security, API and implementation notes
```

## Prerequisite

Install a .NET 10 SDK and confirm it is available:

```powershell
dotnet --info
```

The repository contains `global.json` and targets `net10.0`.

## Start on Windows

From PowerShell in the repository root, run the one-time build and smoke checks:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\bootstrap.ps1
```

Then start the API:

```powershell
.\scripts\run-backend.ps1
```

The run script restores, builds and starts the API. It also sets `DEVSIGNAL_ROOT` to the repository directory. See [`docs/LOCAL_SETUP.md`](docs/LOCAL_SETUP.md) for provider setup, reset instructions and troubleshooting.

Open:

```text
http://localhost:5180/api/v1/dashboard
```

Readiness endpoint:

```text
http://localhost:5180/health/ready
```

## Start on macOS or Linux

```bash
./scripts/run-backend.sh
```

Or run the commands directly:

```bash
export DEVSIGNAL_ROOT="$(pwd)"
dotnet restore DevSignalStudio.sln
dotnet build DevSignalStudio.sln --no-restore
dotnet run --project src/backend/DevSignalStudio.Api
```

## Verify the backend

### Windows

```powershell
.\scripts\verify-backend.ps1
```

### macOS or Linux

```bash
./scripts/verify-backend.sh
```

When a .NET SDK is installed, the verification wrapper also builds the solution and runs 12 runtime smoke checks. Static-only verification is available with:

```bash
python3 scripts/verify-repo.py
```

See [`docs/BACKEND_VERIFICATION.md`](docs/BACKEND_VERIFICATION.md) for exact coverage and the verification status from the build environment used to create this repository.

## First local workflow — no internet and no AI key

The active configuration includes:

- a local JSON source: `curated-local`
- a deterministic AI provider: `mock`
- an AI route: `offline`

After starting the API:

1. Open [`requests/DevSignalStudio.Api.http`](requests/DevSignalStudio.Api.http) in VS Code with REST Client, Visual Studio, Rider or another HTTP client.
2. Run **Start an ingestion run using only local seed data**.
3. Run **List highest-scoring items** and copy an item ID.
4. Put that value into `@itemId` at the top of the request file.
5. Run **Generate a LinkedIn draft from one item**.
6. Inspect the generation run, copy its `draftId`, and review the draft.
7. Edit, validate, approve and export the draft.

The API returns `202 Accepted` for queued ingestion and draft-generation work. Poll the supplied run URL until its status is `completed`, `completedWithWarnings`, `failed` or `cancelled`.

## Core API

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/v1/dashboard` | Counts, latest run and topic activity |
| `GET/POST/PUT` | `/api/v1/sources` | Read and manage source definitions |
| `POST` | `/api/v1/sources/{id}/test` | Check and preview a connector |
| `POST` | `/api/v1/ingestion/runs` | Queue ingestion |
| `GET` | `/api/v1/items` | Search and filter collected content |
| `POST` | `/api/v1/items/manual` | Add a manually reviewed signal |
| `POST` | `/api/v1/drafts` | Queue draft generation |
| `PUT` | `/api/v1/drafts/{id}` | Save a new draft revision |
| `POST` | `/api/v1/drafts/{id}/validate` | Re-run draft validation |
| `POST` | `/api/v1/drafts/{id}/approve` | Approve a valid in-review draft |
| `POST` | `/api/v1/drafts/{id}/reject` | Reject an in-review draft |
| `GET` | `/api/v1/drafts/{id}/export?format=markdown` | Export plain, Markdown, JSON or Mermaid |
| `POST` | `/api/v1/drafts/{id}/mark-published` | Record a post you published manually |
| `GET` | `/api/v1/providers?includeHealth=true` | Provider routes and optional health |
| `GET/PUT` | `/api/v1/topics` | Read or replace the taxonomy |
| `GET` | `/api/v1/recipes` | Read content templates |
| `GET/PUT` | `/api/v1/settings` | Profile, schedule and storage settings |

The detailed contract is in [`docs/API_CONTRACT.md`](docs/API_CONTRACT.md).

## Configuration

Configuration is read from the repository’s `config/` directory and can also be updated through API endpoints.

### `config/topics.json`

The weighted content taxonomy includes:

- .NET and C#
- React and TypeScript
- AI engineering, RAG, agents and evaluation
- MCP and prompt engineering
- system design and data architecture
- Azure, AWS and serverless
- Docker, Kubernetes, DevOps and platform engineering
- performance, reliability and security
- testing and quality engineering
- algorithms, interviews and system-design preparation
- engineering leadership, communication and career growth
- developer productivity and adjacent technology awareness

Tune `defaultMinimumScore`, `dailyCandidateLimit` and `draftCandidateLimit` to control volume.

### `config/sources.json`

Supported connector types:

| Type | Use |
|---|---|
| `rss` | RSS or Atom feeds |
| `stackexchange` | Stack Exchange questions through its JSON API |
| `json-file` | Your own local curated items |
| `http-json` | A compatible remote JSON document |
| `manual` | Explicit capture through the API; never scheduled |

The starter configuration contains official .NET, AWS, Docker, Kubernetes and Stack Overflow feeds, Medium tag feeds, Stack Exchange search, a local JSON source, and optional manual source definitions for LinkedIn, Quora, Edureka and Callback Hell.

Review every source’s terms and content rights. Generic scraping is intentionally not implemented.

### `config/content-recipes.json`

Included recipes:

- LinkedIn practical explainer
- LinkedIn system-design breakdown
- LinkedIn interview question
- LinkedIn leadership lesson
- LinkedIn developer roadmap
- LinkedIn weekly roundup
- Medium technical deep dive

Recipes control the channel, sections, length limits, hashtag range, voice, diagram preference and content requirements.

### `config/profile.json`

Controls:

- author direction, audience and voice
- content boundaries to avoid
- daily scheduler behavior
- automatic draft queueing after ingestion
- `JsonSnapshot` or `MemoryOnly` storage
- storage directory and backup count

The default schedule is 07:00 local machine time, one run per day.

### `config/ai-providers.json`

Providers and routes are separate. A provider stores connection metadata; a route stores ordered fallback choices for each task.

Default routes:

- `offline`: deterministic mock only
- `local-first`: Ollama, then mock
- `balanced`: configured hosted/local providers, then mock

## Use Ollama locally

1. Run Ollama on its default loopback endpoint.
2. Choose a locally installed model.
3. Edit the `ollama-local` provider in `config/ai-providers.json`:
   - set `enabled` to `true`
   - set `model` to the installed model name
4. Set `defaultRoute` to `local-first`, or pass `providerRoute: "local-first"` when generating a draft.
5. Test it with `POST /api/v1/providers/ollama-local/test`.

Only loopback access is relaxed for local AI providers. Arbitrary private-network destinations remain blocked.

## Use OpenAI

1. Set the configured environment variable before starting the API:

```powershell
$env:OPENAI_API_KEY = "your-key"
```

2. In `config/ai-providers.json`, enable `openai` and replace `configure-me` with the model identifier you intend to use.
3. Select a route containing the provider.

The key value is never written to the configuration file.

## Use Anthropic

```powershell
$env:ANTHROPIC_API_KEY = "your-key"
```

Enable `anthropic`, configure the model, and select a route containing it.

## Use another OpenAI-compatible model server

Update `openai-compatible` with the server’s base URL and model name. Loopback servers such as a local model runtime are supported. A key environment variable is optional unless that server requires one.

## Add your own JSON signals

Edit `config/curated-items.json`:

```json
{
  "schemaVersion": 1,
  "items": [
    {
      "id": "unique-note-id",
      "sourceName": "My research note",
      "title": "A practical .NET AI topic",
      "summary": "Why this signal matters",
      "content": "Reviewed notes and evidence",
      "author": "Satya",
      "tags": ["dotnet", "ai", "system-design"],
      "notes": "Add verified external references before making factual claims."
    }
  ]
}
```

Then run ingestion for `curated-local`.

## Persistence

In `JsonSnapshot` mode, runtime data is written under `data/` using atomic replacement and rotating backups. The application restores snapshots after restart and marks interrupted queued/running jobs as failed with a recovery message.

The `data/` directory is intentionally excluded from the distributable archive except for `.gitkeep`.

To run without persistence:

```json
"storage": {
  "mode": "MemoryOnly",
  "directory": "data",
  "backupCount": 0
}
```

Restart the API after changing storage mode or directory.

## Safety and trust boundaries

- Fetched content is treated as untrusted data, not AI instructions.
- Source URLs cannot target private or special-use networks.
- Only explicit loopback AI endpoints are allowed locally.
- Redirect targets are revalidated.
- Response sizes, redirects, item counts and queues are bounded.
- Mermaid click handlers, links, scripts, HTML and unsafe directives are rejected.
- Claims retain source-item IDs and review flags.
- Approval is blocked by validation errors.
- Publishing is always manual.

Read [`docs/SECURITY_AND_TRUST.md`](docs/SECURITY_AND_TRUST.md) and [`docs/BACKEND_IMPLEMENTATION.md`](docs/BACKEND_IMPLEMENTATION.md) before exposing this API beyond your own machine.

## Current phase boundaries

The MVP intentionally does not yet provide a browser UI, automatic social publishing, semantic embeddings, multi-user authentication, generic website scraping or distributed processing. The next implementation phase is the React + Vite + TypeScript review workspace consuming the existing `/api/v1` contract.

## Architecture documents

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/LOCAL_SETUP.md`](docs/LOCAL_SETUP.md)
- [`docs/BACKEND_IMPLEMENTATION.md`](docs/BACKEND_IMPLEMENTATION.md)
- [`docs/API_CONTRACT.md`](docs/API_CONTRACT.md)
- [`docs/SECURITY_AND_TRUST.md`](docs/SECURITY_AND_TRUST.md)
- [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md)
- [`docs/ACCEPTANCE_CRITERIA.md`](docs/ACCEPTANCE_CRITERIA.md)
- [`docs/REFERENCES.md`](docs/REFERENCES.md)
- [`CHANGELOG.md`](CHANGELOG.md)

## React quick-demo workspace

A React + TypeScript demonstration UI is included under `src/frontend`. It exercises the intended local workflow against the repository's curated JSON seed data:

```text
Run daily scan -> rank signals -> create LinkedIn draft -> inspect Mermaid -> validate -> approve
```

For the dependency-free preview harness:

```bash
cd src/frontend
tsc -p tsconfig.demo.json
node server.mjs
```

Then open `http://127.0.0.1:4173`. The preview server uses a deterministic mock API and never publishes or calls an external AI service. The ASP.NET Core API remains the production backend target.
