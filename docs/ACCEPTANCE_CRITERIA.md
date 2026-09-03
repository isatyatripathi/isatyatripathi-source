# MVP Acceptance Criteria

## Local operation

- The application starts without a database server or paid AI account.
- The mock AI provider is selected by default.
- The UI and API bind to loopback by default.
- Restarting the app preserves configuration, collected items, drafts, and review status in `JsonSnapshot` mode.

## Ingestion

- A valid custom JSON file is ingested without code changes.
- RSS and Atom fixtures produce the same canonical item model.
- Stack Exchange responses are parsed through a dedicated connector.
- Duplicate URLs and materially identical entries do not create duplicate candidates.
- Every item contains provenance and a visible score explanation.
- One failed source does not fail the entire run.

## Relevance

- Topic definitions and weights can be changed in JSON and reloaded safely.
- Deterministic scoring works with AI disabled.
- The system explains matched terms, freshness, source weight, and penalties.
- The daily candidate cap and minimum score are configurable.

## Drafting

- The mock provider generates a valid draft and Mermaid diagram.
- At least one real provider can be configured without changing application code.
- Invalid provider output is repaired only a bounded number of times and then falls back safely.
- Every draft records source references, model/provider metadata, recipe version, and revision.
- Unsupported claims block approval unless converted to opinion or removed.

## Review and export

- Draft text and Mermaid source are editable.
- A stale revision cannot overwrite a newer revision.
- Drafts can be approved, rejected with a reason, archived, and marked manually published.
- Approved drafts export to plain text and Markdown.
- Mermaid renders under strict security and exports to SVG; browser-side PNG export is available.

## Safety

- API keys never appear in configuration responses or logs.
- External source URLs cannot reach private network ranges under default settings.
- Source HTML cannot execute in the UI.
- Unsafe Mermaid directives are rejected.
- There is no automatic LinkedIn or Medium publishing path in v1.

## Quality

- Domain, application, connector, API, and frontend tests cover the critical path.
- An end-to-end test runs JSON ingestion → scoring → mock generation → validation → approval → export.
