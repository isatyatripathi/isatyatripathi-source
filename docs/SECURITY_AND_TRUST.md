# Security, Safety, and Trust Model

## Trust boundaries

1. **External content is untrusted.** Feed entries, copied text, HTML, API payloads, and AI outputs may be malicious or misleading.
2. **Configuration is privileged.** A configurable URL can become an SSRF path; a configurable provider can receive private content.
3. **The browser is a rendering boundary.** Markdown, HTML fragments, and Mermaid must not become script execution.
4. **Publication is irreversible outside the app.** Approval and manual posting remain separate actions.

## Required controls

### Prompt injection

- Place external text in clearly delimited data sections.
- System prompts explicitly state that source content is evidence, not instructions.
- Never let a model choose or invoke application tools based solely on ingested text.
- Do not include workstation files, environment variables, or unrelated drafts in model context.
- Treat model-produced URLs, commands, and claims as untrusted until validated.

### SSRF and connector URLs

- Allow only `https` by default; allow `http` only for explicit loopback services such as local Ollama.
- Resolve and reject private, link-local, metadata-service, and loopback addresses for external sources.
- Re-check resolved addresses after redirects.
- Limit redirects, response size, decompression ratio, duration, and content type.
- Keep connector and provider allowlists separate so a source cannot target the local AI endpoint.

### Cross-site scripting and Mermaid

- Render source excerpts as text or sanitized Markdown; never inject source HTML directly.
- Initialize Mermaid with strict security.
- Reject Mermaid `click` directives, unsafe initialization blocks, external images, and oversized diagrams.
- Use a restrictive Content Security Policy in the production build.

### Secrets

- Keep keys in environment variables or .NET user secrets.
- Store only the environment-variable name in configuration.
- Redact authorization headers and secret-like fields from logs and API responses.
- Do not send source content to a cloud provider until the user enables that route.

### Content integrity and attribution

- Preserve source title, author, publisher, URL, publication time, and retrieval time.
- Store short excerpts, summaries, and metadata rather than entire third-party articles.
- Maintain a claim ledger with `Supported`, `Opinion`, `NeedsReview`, or `Unsupported` status.
- Flag generated statistics, quotations, release dates, and performance claims for explicit support.
- Keep generation metadata and draft revision history.

### Publication safety

- No scheduled or automatic social posting in v1.
- Approval requires the latest revision and passing validations.
- Exported drafts include an unobtrusive local metadata file containing references and generation details, even when the public-facing text omits them.

## Local network posture

- Bind to loopback by default.
- Do not expose the API on the LAN without authentication and TLS.
- CORS permits only configured local UI origins.
- A future remote deployment requires authentication, authorization, CSRF review, rate limiting, and a new threat model.
