import http from "node:http";
import { readFileSync, writeFileSync, existsSync, statSync } from "node:fs";
import { extname, join, normalize, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = normalize(join(here, "..", ".."));
const distRoot = join(here, "dist");
const curated = JSON.parse(readFileSync(join(repoRoot, "config", "curated-items.json"), "utf8"));
const sourceConfig = JSON.parse(readFileSync(join(repoRoot, "config", "sources.json"), "utf8"));

const scores = [92, 89, 84];
const ages = ["6 min ago", "11 min ago", "18 min ago"];
const items = curated.items.map((item, index) => ({
  id: item.id,
  sourceName: item.sourceName,
  title: item.title,
  summary: item.summary,
  tags: item.tags,
  score: scores[index] ?? 78,
  age: ages[index] ?? "today"
}));

const sources = sourceConfig.sources.map(source => ({
  id: source.id,
  name: source.name,
  connectorType: source.connectorType,
  enabled: source.enabled,
  trustWeight: source.trustWeight
}));
const providerConfig = JSON.parse(readFileSync(join(repoRoot, "config", "ai-providers.json"), "utf8"));

let lastRun = "2 min ago";
let draftStatus = "inReview";
let runCounter = 0;

const hook = "Provider abstraction is useful—until it erases the capabilities you actually need.";
const body = `A clean AI layer in ASP.NET Core should not pretend every model provider is identical.

The boundary I would keep:

1. Route by task, not by vendor. Drafting, classification, embeddings, and evaluation can have different provider chains.

2. Expose capabilities explicitly. Structured output, tool calling, streaming, and usage reporting should be discoverable—not hidden behind a lowest-common-denominator interface.

3. Keep operational safeguards outside the adapter. Timeouts, cancellation, retries, validation, and secret handling belong to the application boundary.

4. Retain a deterministic local fallback. It makes development and tests repeatable even when a hosted model is unavailable.

The practical test: can you switch the default model without rewriting application workflows, while still opting into provider-specific features deliberately?

That balance—portable workflows with visible capabilities—is more useful than pretending every provider behaves the same.

What is one capability your current AI abstraction makes difficult to use?`;

function getDraft() {
  const hashtags = ["#DotNet", "#AIEngineering", "#SoftwareArchitecture", "#AspNetCore"];
  return {
    id: "demo-draft",
    title: "A practical boundary for AI-provider abstractions in ASP.NET Core",
    hook,
    body,
    hashtags,
    mermaid: `flowchart LR\n  A[Content task] --> B{Route by capability}\n  B --> C[Ollama]\n  B --> D[OpenAI]\n  B --> E[Anthropic]\n  C --> F[Validate result]\n  D --> F\n  E --> F\n  F --> G[Human review]`,
    status: draftStatus,
    sourceTitle: curated.items[0].title,
    sourceName: curated.items[0].sourceName,
    characterCount: `${hook}\n\n${body}\n\n${hashtags.join(" ")}`.length
  };
}

function json(res, status, value) {
  const body = JSON.stringify(value);
  res.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": Buffer.byteLength(body),
    "Cache-Control": "no-store"
  });
  res.end(body);
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    let data = "";
    req.on("data", chunk => { data += chunk; if (data.length > 1_000_000) reject(new Error("Request too large")); });
    req.on("end", () => {
      if (!data) return resolve({});
      try { resolve(JSON.parse(data)); } catch (error) { reject(error); }
    });
    req.on("error", reject);
  });
}

const mime = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png"
};

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url ?? "/", "http://127.0.0.1");
  const path = url.pathname;

  try {
    if (req.method === "GET" && path === "/health/ready") {
      return json(res, 200, { status: "ready", mode: "local-demo", provider: "deterministic-mock" });
    }
    if (req.method === "GET" && path === "/api/v1/dashboard") {
      return json(res, 200, {
        signals: items.length,
        candidates: items.filter(item => item.score >= 70).length,
        drafts: 1,
        activeSources: sources.filter(source => source.enabled).length,
        totalSources: sources.length,
        lastRun
      });
    }
    if (req.method === "GET" && path === "/api/v1/items") {
      return json(res, 200, { items, total: items.length });
    }
    if (req.method === "GET" && path === "/api/v1/sources") {
      return json(res, 200, { items: sources, total: sources.length });
    }
    if (req.method === "GET" && path === "/api/v1/providers") {
      try {
        const backendResponse = await fetch(`http://127.0.0.1:5180/api/v1/providers${url.search}`, { method: "GET" });
        return json(res, backendResponse.status, await backendResponse.json());
      } catch {
        return json(res, 503, { title: "Backend API is not reachable" });
      }
    }
    if (req.method === "POST" && path.startsWith("/api/v1/providers/") && path.endsWith("/test")) {
      const providerId = path.split("/")[4];
      const provider = providerConfig.providers.find(item => item.id === providerId);
      if (!provider) return json(res, 404, { title: "Provider not found" });
      try {
        const backendResponse = await fetch(`http://127.0.0.1:5180/api/v1/providers/${providerId}/test`, { method: "POST" });
        const result = await backendResponse.json();
        return json(res, backendResponse.status, result);
      } catch (error) {
        return json(res, 503, {
          providerId,
          status: "unhealthy",
          message: "The backend API is not reachable. Start it on http://127.0.0.1:5180 before testing providers."
        });
      }
    }
    if (req.method === "PUT" && path.startsWith("/api/v1/providers/")) {
      const providerId = path.split("/")[4];
      const provider = providerConfig.providers.find(item => item.id === providerId);
      if (!provider) return json(res, 404, { title: "Provider not found" });
      const updated = await readBody(req);
      Object.assign(provider, {
        displayName: updated.displayName ?? provider.displayName,
        model: updated.model ?? provider.model,
        baseUrl: updated.baseUrl ?? provider.baseUrl,
        enabled: updated.enabled ?? provider.enabled
      });
      writeFileSync(join(repoRoot, "config", "ai-providers.json"), `${JSON.stringify(providerConfig, null, 2)}\n`);
      return json(res, 200, provider);
    }
    if (req.method === "POST" && path === "/api/v1/ingestion/runs") {
      await readBody(req);
      runCounter += 1;
      lastRun = "just now";
      return json(res, 202, { runId: `demo-run-${runCounter}`, status: "completed", collected: 3, selected: 3, duplicates: 0 });
    }
    if (req.method === "POST" && path === "/api/v1/drafts") {
      await readBody(req);
      draftStatus = "inReview";
      return json(res, 202, { generationRunId: "demo-generation-1", status: "completed", draftId: "demo-draft" });
    }
    if (req.method === "GET" && path === "/api/v1/drafts/demo-draft") {
      return json(res, 200, getDraft());
    }
    if (req.method === "POST" && path === "/api/v1/drafts/demo-draft/approve") {
      draftStatus = "approved";
      return json(res, 200, getDraft());
    }

    const relative = path === "/" ? "index.html" : path.replace(/^\//, "");
    const filePath = normalize(join(distRoot, relative));
    if (!filePath.startsWith(distRoot)) {
      res.writeHead(403); return res.end("Forbidden");
    }
    const resolved = existsSync(filePath) && statSync(filePath).isFile() ? filePath : join(distRoot, "index.html");
    const content = readFileSync(resolved);
    res.writeHead(200, { "Content-Type": mime[extname(resolved)] ?? "application/octet-stream", "Cache-Control": "no-cache" });
    res.end(content);
  } catch (error) {
    json(res, 500, { title: "Demo server error", detail: error instanceof Error ? error.message : String(error) });
  }
});

const port = Number(process.env.PORT ?? 4173);
server.listen(port, "127.0.0.1", () => {
  console.log(`DevSignal Studio demo running at http://127.0.0.1:${port}`);
});
