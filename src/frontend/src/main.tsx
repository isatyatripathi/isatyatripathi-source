declare const React: any;
declare const ReactDOM: any;
declare namespace JSX { interface IntrinsicElements { [elementName: string]: any; } }

type ViewName = "overview" | "signals" | "drafts" | "sources" | "topics" | "settings" | "ai-models";

interface SignalItem {
  id: string;
  sourceName: string;
  title: string;
  summary: string;
  tags: string[];
  score: number;
  age: string;
}

interface SourceItem {
  id: string;
  name: string;
  connectorType: string;
  enabled: boolean;
  trustWeight: number;
}

interface DashboardData {
  signals: number;
  candidates: number;
  drafts: number;
  activeSources: number;
  totalSources: number;
  lastRun: string;
}

interface DraftData {
  id: string;
  title: string;
  hook: string;
  body: string;
  hashtags: string[];
  mermaid: string;
  status: string;
  sourceTitle: string;
  sourceName: string;
  characterCount: number;
}

interface AiProviderDefinition {
  id: string;
  type: string;
  displayName: string;
  enabled: boolean;
  model: string;
  baseUrl?: string;
  apiKeyEnvironmentVariable?: string;
  capabilities: string[];
  settings: Record<string, any>;
}

interface AiProvidersDocument {
  schemaVersion: number;
  defaultRoute: string;
  providers: AiProviderDefinition[];
  routes: AiRouteDefinition[];
}

interface AiRouteDefinition {
  id: string;
  tasks: Record<string, string[]>;
}

interface AiProviderHealth {
  providerId: string;
  isHealthy: boolean;
  lastChecked: string;
  error?: string;
}

interface AppState {
  view: ViewName;
  items: SignalItem[];
  sources: SourceItem[];
  dashboard: DashboardData;
  draft: DraftData | null;
  scanRunning: boolean;
  scanPhase: number;
  generating: boolean;
  toast: string;
  aiProviders: AiProvidersDocument | null;
  aiProviderHealth: Record<string, AiProviderHealth> | null;
  editingProvider: AiProviderDefinition | null;
  testingProviders: Set<string>;
  savingProvider: boolean;
  selectedProviderIds: string[];
  providerLoadError: string | null;
}

const navItems: Array<{id: ViewName; label: string; icon: string}> = [
  { id: "overview", label: "Overview", icon: "◫" },
  { id: "signals", label: "Content signals", icon: "⌁" },
  { id: "drafts", label: "Review queue", icon: "✎" },
  { id: "sources", label: "Data sources", icon: "◎" },
  { id: "topics", label: "Topic map", icon: "◇" },
  { id: "ai-models", label: "AI Models", icon: "🤖" },
  { id: "settings", label: "Settings", icon: "⚙" }
];

const pipelineLabels = ["Collect", "Normalize", "Deduplicate", "Score", "Draft", "Review"];

class App extends React.Component<{}, AppState> {
  private toastTimer: any;

  constructor(props: {}) {
    super(props);
    this.state = {
      view: "overview",
      items: [],
      sources: [],
      dashboard: { signals: 0, candidates: 0, drafts: 0, activeSources: 0, totalSources: 0, lastRun: "Not run" },
      draft: null,
      scanRunning: false,
      scanPhase: 5,
      generating: false,
      toast: "",
      aiProviders: null,
      aiProviderHealth: null,
      editingProvider: null,
      testingProviders: new Set(),
      savingProvider: false,
      selectedProviderIds: ["mock"],
      providerLoadError: null
    };
  }

  componentDidMount() {
    this.loadData();
    this.loadAiProviders();
  }

  async loadData() {
    try {
      const [dashboard, items, sources, draft, providersResponse] = await Promise.all([
        fetch("/api/v1/dashboard").then(r => r.json()),
        fetch("/api/v1/items").then(r => r.json()),
        fetch("/api/v1/sources").then(r => r.json()),
        fetch("/api/v1/drafts/demo-draft").then(r => r.json()),
        fetch("/api/v1/providers?includeHealth=true").then(r => r.json()).catch(() => ({}))
      ]);
      
      let aiProviders = null;
      let aiProviderHealth = null;
      
      // Handle different response formats from the API
      if (providersResponse.configuration) {
        aiProviders = providersResponse.configuration;
        aiProviderHealth = {};
        if (Array.isArray(providersResponse.health)) {
          providersResponse.health.forEach((health: AiProviderHealth) => {
            aiProviderHealth[health.providerId] = health;
          });
        }
      } else if (providersResponse.providers) {
        aiProviders = providersResponse;
        aiProviderHealth = {};
      } else if (providersResponse.schemaVersion !== undefined) {
        // Direct AiProvidersDocument response
        aiProviders = providersResponse;
        aiProviderHealth = {};
      }
      
      this.setState({ 
        dashboard, 
        items: items.items, 
        sources: sources.items, 
        draft, 
        aiProviders, 
        aiProviderHealth 
      });
    } catch (error) {
      console.error("Error loading data:", error);
      // Still set partial state
      this.setState({ 
        dashboard: { signals: 0, candidates: 0, drafts: 0, activeSources: 0, totalSources: 0, lastRun: "Error" },
        items: [],
        sources: [],
        draft: null
      });
    }
  }

  showToast(message: string) {
    if (this.toastTimer) window.clearTimeout(this.toastTimer);
    this.setState({ toast: message });
    this.toastTimer = window.setTimeout(() => this.setState({ toast: "" }), 2800);
  }

  setView(view: ViewName) {
    this.setState({ view });
    // Load AI providers data when switching to that view
    if (view === "ai-models" && !this.state.aiProviders) {
      this.loadAiProviders();
    }
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  async runScan() {
    if (this.state.scanRunning) return;
    this.setState({ scanRunning: true, scanPhase: 0 });
    await fetch("/api/v1/ingestion/runs", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ sourceIds: ["curated-local"], generateDrafts: false }) });

    const phases = [1, 2, 3, 4, 5, 6];
    for (const phase of phases) {
      await new Promise(resolve => window.setTimeout(resolve, 360));
      this.setState({ scanPhase: phase });
    }
    await this.loadData();
    this.setState({ scanRunning: false, scanPhase: 6 });
    this.showToast("Daily scan complete — 3 high-relevance signals are ready.");
  }

  async generateDraft(itemId: string) {
    if (this.state.generating) return;
    this.setState({ generating: true });
    const configuredProviders = this.state.aiProviders?.providers || [];
    const selectedProviderIds = this.state.selectedProviderIds.includes("all")
      ? configuredProviders.filter(provider => provider.enabled).map(provider => provider.id)
      : this.state.selectedProviderIds;
    const providerRequests = selectedProviderIds.length > 0 ? selectedProviderIds : ["mock"];
    try {
      const responses = await Promise.all(providerRequests.map(providerId => fetch("/api/v1/drafts", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ contentItemIds: [itemId], recipeId: "linkedin-explainer", providerRoute: providerId })
      })));
      const failedResponse = responses.find(response => !response.ok);
      if (failedResponse) throw new Error(`Draft generation failed (${failedResponse.status})`);
      await new Promise(resolve => window.setTimeout(resolve, 900));
      const draftResponse = await fetch("/api/v1/drafts/demo-draft");
      if (!draftResponse.ok) throw new Error(`Could not load generated draft (${draftResponse.status})`);
      const draft = await draftResponse.json();
      this.setState({ draft, view: "drafts" });
      const providerNames = providerRequests.map(providerId => configuredProviders.find(provider => provider.id === providerId)?.displayName || providerId);
      this.showToast(`Draft generated using ${providerNames.join(", ")} in parallel with a safe Mermaid diagram and source mapping.`);
      window.scrollTo({ top: 0, behavior: "smooth" });
    } catch (error) {
      this.showToast(error instanceof Error ? error.message : "Draft generation failed");
    } finally {
      this.setState({ generating: false });
    }
  }

  async approveDraft() {
    const draft = await fetch("/api/v1/drafts/demo-draft/approve", { method: "POST" }).then(r => r.json());
    this.setState({ draft });
    this.showToast("Draft approved. It is ready for your manual LinkedIn post.");
  }

  exportDraft() {
    const draft = this.state.draft;
    if (!draft) return;
    const content = `${draft.hook}\n\n${draft.body}\n\n${draft.hashtags.join(" ")}\n\n---\nMermaid\n${draft.mermaid}`;
    const blob = new Blob([content], { type: "text/plain" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = "devsignal-linkedin-draft.txt";
    anchor.click();
    URL.revokeObjectURL(url);
    this.showToast("Draft exported as a text file.");
  }

  renderSidebar() {
    return <aside className="sidebar">
      <div className="brand">
        <div className="brand-mark">D</div>
        <div><div className="brand-name">DevSignal Studio</div><div className="brand-sub">AI-powered content intelligence</div></div>
      </div>
      <div className="nav-title">Workspace</div>
      <nav className="nav">
        {navItems.map(item => <button key={item.id} data-nav={item.id} className={`nav-btn ${this.state.view === item.id ? "active" : ""}`} onClick={() => this.setView(item.id)}>
          <span className="nav-icon">{item.icon}</span><span className="nav-label">{item.label}</span>
        </button>)}
      </nav>
      <div className="spacer"></div>
      <div className="local-box">
        <div className="local-line"><span className="pulse"></span>Local workspace online</div>
        <p>Offline AI route · JSON snapshots<br/>No automatic publishing</p>
      </div>
      <div className="user-card">
        <div className="avatar">SA</div>
        <div><div className="user-name">Satya</div><div className="user-role">Senior Software Engineering Lead</div></div>
      </div>
    </aside>;
  }

  renderTopbar(section: string) {
    const selectedProviderDisplay = this.state.selectedProviderIds.includes("all")
      ? "All enabled models"
      : this.state.selectedProviderIds.map(id => this.state.aiProviders?.providers.find(provider => provider.id === id)?.displayName || id).join(", ") || "No provider selected";
    
    return <header className="topbar">
      <div className="crumb"><span>Workspace</span><span>/</span><strong>{section}</strong></div>
      <div className="top-actions">
        <div style={{display: "flex", gap: "12px", alignItems: "center"}}>
          <div style={{display: "flex", alignItems: "center", gap: "8px", padding: "6px 12px", backgroundColor: "#f3f4f6", borderRadius: "6px", fontSize: "12px"}}>
            <span style={{fontWeight: 500}}>Using:</span>
            <select 
              multiple
              value={this.state.selectedProviderIds}
              onChange={(e: any) => {
                const values = Array.from(e.target.selectedOptions as HTMLCollectionOf<HTMLOptionElement>).map(option => option.value);
                this.setState({ selectedProviderIds: values.length > 0 ? values : ["mock"] });
              }}
              style={{
                padding: "4px 8px",
                borderRadius: "4px",
                border: "1px solid #d1d5db",
                backgroundColor: "#fff",
                fontSize: "12px",
                fontWeight: 600,
                cursor: "pointer",
                color: "#059669"
              }}
            >
              <option value="all">All enabled models (parallel)</option>
              {(this.state.aiProviders?.providers || []).map(provider => (
                <option key={provider.id} value={provider.id} disabled={!provider.enabled}>
                  {provider.displayName} {provider.enabled ? "" : "(disabled)"}
                </option>
              ))}
            </select>
          </div>
          <button className="icon-btn" aria-label="Notifications">◌</button>
          <button className="icon-btn" aria-label="Help">?</button>
        </div>
      </div>
    </header>;
  }

  renderStats() {
    const d = this.state.dashboard;
    const cards = [
      ["Signals collected", d.signals, "⌁", "3 added by the latest run", true],
      ["Draft candidates", d.candidates, "✦", "Minimum relevance score: 70", true],
      ["Review queue", d.drafts, "✎", "Human approval required", false],
      ["Active sources", `${d.activeSources}/${d.totalSources}`, "◎", "Configurable connectors", false]
    ];
    return <div className="stats">
      {cards.map((card: any, index: number) => <div className="stat" key={index}>
        <div className="stat-top"><span>{card[0]}</span><span className="stat-icon">{card[2]}</span></div>
        <div className="stat-value">{card[1]}</div>
        <div className={`stat-foot ${card[4] ? "good" : ""}`}>{card[4] ? "↑ " : ""}{card[3]}</div>
      </div>)}
    </div>;
  }

  renderPipeline() {
    const phase = this.state.scanPhase;
    const fill = phase <= 0 ? 0 : Math.min(86, ((phase - 1) / 5) * 86);
    return <div className="panel">
      <div className="panel-head"><div><div className="panel-title">Daily intelligence pipeline</div><div className="panel-sub">Filter before AI · preserve provenance · review before publishing</div></div><button className="text-btn">Run history →</button></div>
      <div className="pipeline">
        <div className="pipeline-track">
          <div className="pipeline-fill" style={{ width: `${fill}%` }}></div>
          {pipelineLabels.map((label, i) => {
            const step = i + 1;
            const cls = phase > step ? "done" : phase === step ? "active" : "";
            return <div className={`step ${cls}`} key={label}><div className="step-dot">{phase > step ? "✓" : step}</div><div className="step-label">{label}</div></div>;
          })}
        </div>
        <div className="pipeline-summary">
          <span>{this.state.scanRunning ? <span><span className="spinner" style={{display:"inline-block", marginRight:"7px", verticalAlign:"middle"}}></span>Processing local source…</span> : <span>Last completed <strong>{this.state.dashboard.lastRun}</strong></span>}</span>
          <span><strong>3</strong> collected · <strong>3</strong> selected · <strong>0</strong> duplicates</span>
        </div>
      </div>
    </div>;
  }

  renderSignals() {
    return <div className="panel" style={{marginTop:"15px"}}>
      <div className="panel-head"><div><div className="panel-title">Top signals for your profile</div><div className="panel-sub">Ranked for full-stack AI-powered .NET engineering</div></div><button className="text-btn" onClick={() => this.setView("signals")}>View all →</button></div>
      <div className="signals">
        {this.state.items.map(item => <div className="signal" key={item.id}>
          <div className="score" style={{"--score": item.score} as any}><span>{item.score}</span></div>
          <div>
            <div className="signal-source"><span className="source-dot"></span>{item.sourceName} · {item.age}</div>
            <h3 className="signal-title">{item.title}</h3>
            <p className="signal-summary">{item.summary}</p>
            <div className="tags">{item.tags.slice(0,4).map(tag => <span className="tag" key={tag}>{tag}</span>)}</div>
          </div>
          <button className="draft-btn" data-action={`draft-${item.id}`} onClick={() => this.generateDraft(item.id)}>{this.state.generating ? "Generating…" : "Create draft →"}</button>
        </div>)}
      </div>
    </div>;
  }

  renderOverview() {
    const draft = this.state.draft;
    const activity = [32, 46, 38, 64, 50, 76, 92];
    return <div>
      {this.renderTopbar("Overview")}
      <div className="page">
        <section className="hero">
          <div><div className="eyebrow">Monday intelligence brief</div><h1>Good morning, Satya.</h1><p>Your local sandbox found three strong ideas connecting AI engineering, .NET architecture, security, and senior-level communication.</p></div>
          <button className="primary" data-action="run-scan" disabled={this.state.scanRunning} onClick={() => this.runScan()}>{this.state.scanRunning ? <span className="spinner"></span> : "✦"}{this.state.scanRunning ? "Scanning sources…" : "Run daily scan"}</button>
        </section>
        {this.renderStats()}
        <div className="grid">
          <div>{this.renderPipeline()}{this.renderSignals()}</div>
          <div className="side">
            <div className="panel review">
              <div className="review-top"><div><div className="panel-title">Next to review</div><div className="panel-sub">LinkedIn practical explainer</div></div><span className={`status ${draft && draft.status === "approved" ? "approved" : "reviewing"}`}>{draft && draft.status === "approved" ? "Approved" : "In review"}</span></div>
              <div className="review-title">{draft ? draft.title : "AI-provider abstraction in ASP.NET Core"}</div>
              <p className="review-copy">A practical draft about keeping providers replaceable without erasing useful model capabilities.</p>
              <div className="review-meta"><span>{draft ? draft.characterCount : 1248} chars · Mermaid included</span><button className="review-link" data-action="open-review" onClick={() => this.setView("drafts")}>Open review →</button></div>
            </div>
            <div className="panel">
              <div className="panel-head"><div><div className="panel-title">Topic coverage</div><div className="panel-sub">Latest seven days</div></div></div>
              <div className="coverage">
                <div className="donut"><div className="donut-text"><div className="donut-value">12</div><div className="donut-label">active topics</div></div></div>
                <div className="legend">
                  {[["#8b5cf6","AI + .NET","38%"],["#22d3ee","System design","23%"],["#34d399","Leadership","17%"],["#fbbf24","Cloud + DevOps","13%"],["#fb7185","Security","9%"]].map((x:any) => <div className="legend-row" key={x[1]}><span className="legend-dot" style={{background:x[0]}}></span><span>{x[1]}</span><strong>{x[2]}</strong></div>)}
                </div>
              </div>
            </div>
            <div className="panel">
              <div className="panel-head"><div><div className="panel-title">Content momentum</div><div className="panel-sub">Signals selected per day</div></div></div>
              <div className="activity"><div className="bars">{activity.map((h,i) => <div className="bar-wrap" key={i}><div className="bar" style={{height:`${h}%`}}></div><div className="bar-label">{["T","W","T","F","S","S","M"][i]}</div></div>)}</div></div>
            </div>
          </div>
        </div>
      </div>
    </div>;
  }

  renderDiagram() {
    return <div className="diagram">
      <svg viewBox="0 0 520 292" role="img" aria-label="AI provider routing workflow">
        <defs><marker id="arrow" markerWidth="7" markerHeight="7" refX="5" refY="3.5" orient="auto"><path d="M0,0 L0,7 L7,3.5 z" fill="#5b687d"></path></marker></defs>
        <path className="edge" d="M112 145 L174 145"></path>
        <path className="edge" d="M262 145 C292 145 288 58 320 58"></path>
        <path className="edge" d="M262 145 L320 145"></path>
        <path className="edge" d="M262 145 C292 145 288 232 320 232"></path>
        <path className="edge" d="M398 58 C438 58 430 112 452 128"></path>
        <path className="edge" d="M398 145 L452 145"></path>
        <path className="edge" d="M398 232 C438 232 430 177 452 162"></path>
        <rect className="node" x="28" y="119" rx="9" width="84" height="52"></rect><text className="node-text" x="70" y="140">Content</text><text className="node-text" x="70" y="154">task</text>
        <rect className="node" x="174" y="119" rx="9" width="88" height="52"></rect><text className="node-text" x="218" y="140">Route by</text><text className="node-text" x="218" y="154">capability</text>
        <rect className="node alt" x="320" y="38" rx="9" width="78" height="40"></rect><text className="node-text" x="359" y="62">Ollama</text>
        <rect className="node alt" x="320" y="125" rx="9" width="78" height="40"></rect><text className="node-text" x="359" y="149">OpenAI</text>
        <rect className="node alt" x="320" y="212" rx="9" width="78" height="40"></rect><text className="node-text" x="359" y="236">Anthropic</text>
        <rect className="node good" x="452" y="119" rx="9" width="48" height="52"></rect><text className="node-text" x="476" y="140">Validate</text><text className="node-text" x="476" y="154">result</text>
      </svg>
    </div>;
  }

  renderDraft() {
    const d = this.state.draft;
    if (!d) return <div>{this.renderTopbar("Review queue")}<div className="page"><section className="hero"><div><h1>Preparing draft…</h1></div></section></div></div>;
    const approved = d.status === "approved";
    return <div>
      {this.renderTopbar("Review queue / Draft")}
      <div className="page">
        <div className="draft-header">
          <div className="draft-heading"><button className="back" data-action="back" onClick={() => this.setView("overview")}>←</button><div><h1>Review LinkedIn draft</h1><p>Generated locally · deterministic provider · source-backed · revision 1</p></div></div>
          <div className="draft-actions"><span className={`status ${approved ? "approved" : "reviewing"}`}>{approved ? "Approved" : "In review"}</span><button className="secondary" onClick={() => this.exportDraft()}>⇩ Export</button><button className="success" data-action="approve" onClick={() => this.approveDraft()}>{approved ? "✓ Approved" : "✓ Approve draft"}</button></div>
        </div>
        <div className="draft-grid">
          <div className="panel">
            <div className="editor-toolbar"><div className="tabs"><button className="tab active">Post preview</button><button className="tab">Edit content</button><button className="tab">Claims</button></div><div className="char">{d.characterCount} / 2,800 characters</div></div>
            <div className="post-wrap">
              <div className="linkedin">
                <div className="li-head"><div className="li-avatar">SA</div><div><div className="li-name">Satya</div><div className="li-role">Senior Software Engineering Lead · Just now · ◉</div></div></div>
                <div className="li-body"><div className="li-hook">{d.hook}</div>{"\n\n"}{d.body}<div className="li-tags">{d.hashtags.join(" ")}</div></div>
                <div className="li-footer"><span>♡ Like</span><span>◯ Comment</span><span>↗ Repost</span><span>✈ Send</span></div>
              </div>
            </div>
          </div>
          <div className="inspector">
            <div className="panel">
              <div className="panel-head"><div><div className="panel-title">Architecture diagram</div><div className="panel-sub">Editable Mermaid · safe rendering mode</div></div><span className="status approved">Validated</span></div>
              <div className="diagram-stage">{this.renderDiagram()}</div>
              <pre className="code">{d.mermaid}</pre>
            </div>
            <div className="panel">
              <div className="panel-head"><div><div className="panel-title">Quality gates</div><div className="panel-sub">Human review remains the final control</div></div><span className="status approved">4 passed</span></div>
              <div className="validation">
                {[["Length and format","Within LinkedIn recipe limit","PASS"],["Claims and sources","No unsupported external claims","PASS"],["Diagram safety","No links, scripts, or click handlers","PASS"],["Voice alignment","Practical, clear, evidence-aware","PASS"]].map((x:any) => <div className="check-row" key={x[0]}><span className="check">✓</span><div><div className="check-title">{x[0]}</div><div className="check-note">{x[1]}</div></div><span className="check-value">{x[2]}</span></div>)}
              </div>
              <div className="source-ref"><label>Primary source</label><strong>{d.sourceTitle}</strong><span>{d.sourceName} · local curated JSON · provenance retained</span></div>
            </div>
          </div>
        </div>
      </div>
    </div>;
  }

  renderSources() {
    return <div>{this.renderTopbar("Data sources")}<div className="page">
      <section className="hero"><div><div className="eyebrow">Configurable connectors</div><h1>Source catalog</h1><p>Combine official RSS feeds, Stack Exchange, local JSON, remote JSON, and explicit manual capture without depending on one AI provider.</p></div><button className="primary">＋ Add source</button></section>
      <div className="source-grid">{this.state.sources.map(source => <div className="source-card" key={source.id}><div className="source-logo">{source.name.substring(0,2).toUpperCase()}</div><div><div className="source-name">{source.name}</div><div className="source-meta">{source.connectorType} · trust {Math.round(source.trustWeight*100)}% · daily</div></div><div className={`health ${source.enabled ? "" : "off"}`}>{source.enabled ? "● ENABLED" : "○ DISABLED"}</div></div>)}</div>
    </div></div>;
  }

  renderPlaceholder(title: string, subtitle: string) {
    return <div>{this.renderTopbar(title)}<div className="page"><section className="hero"><div><div className="eyebrow">DevSignal Studio</div><h1>{title}</h1><p>{subtitle}</p></div></section>{this.renderSignals()}</div></div>;
  }

  async loadAiProviders() {
    try {
      this.setState({ providerLoadError: null });
      
      const response = await fetch("/api/v1/providers?includeHealth=true");
      
      if (!response.ok) {
        throw new Error(`API returned ${response.status}: ${response.statusText}`);
      }
      
      const contentType = response.headers.get("content-type") || "";
      if (!contentType.includes("application/json")) {
        const text = await response.text();
        console.error("Expected JSON but got:", contentType, text.substring(0, 200));
        throw new Error(`Expected JSON response but got ${contentType}. Response: ${text.substring(0, 100)}`);
      }
      
      let data: any;
      try {
        data = await response.json();
      } catch (parseError) {
        const text = await response.clone().text();
        console.error("Failed to parse JSON:", parseError, "Response text:", text.substring(0, 300));
        throw new Error(`Invalid JSON response: ${text.substring(0, 150)}`);
      }
      
      let aiProviders = null;
      let aiProviderHealth: Record<string, AiProviderHealth> = {};
      
      if (data.configuration) {
        aiProviders = data.configuration;
      } else if (data.providers) {
        aiProviders = data;
      } else if (data.schemaVersion !== undefined) {
        aiProviders = data;
      } else {
        throw new Error("API returned data in unexpected format");
      }
      
      if (Array.isArray(data.health)) {
        data.health.forEach((health: AiProviderHealth) => {
          aiProviderHealth[health.providerId] = health;
        });
      }
      
      // Set default provider if not already set
      let defaultProviders = this.state.selectedProviderIds;
      if (!defaultProviders.some(id => aiProviders?.providers.some(provider => provider.id === id))) {
        const enabledProvider = aiProviders?.providers.find(p => p.enabled);
        defaultProviders = [enabledProvider?.id || "mock"];
      }
      
      this.setState({ aiProviders, aiProviderHealth, selectedProviderIds: defaultProviders });
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : "Failed to load AI providers";
      console.error("Error loading AI providers:", error);
      this.setState({ 
        providerLoadError: errorMessage,
        aiProviders: { schemaVersion: 1, defaultRoute: "offline", providers: [], routes: [] }
      });
      this.showToast("Error loading provider configuration: " + errorMessage);
    }
  }

  async testAiProvider(providerId: string) {
    const testing = new Set(this.state.testingProviders);
    testing.add(providerId);
    this.setState({ testingProviders: testing });
    
    try {
      const response = await fetch(`/api/v1/providers/${providerId}/test`, { method: "POST" });
      if (!response.ok) throw new Error(`Provider test failed (${response.status})`);
      const result = await response.json();
      const health = {
        ...result,
        isHealthy: result.isHealthy ?? result.status === "healthy",
        lastChecked: result.lastChecked ?? result.checkedAt,
        error: result.error ?? result.message
      };
      const healthMap = this.state.aiProviderHealth || {};
      healthMap[providerId] = health;
      this.setState({ aiProviderHealth: healthMap });
      this.showToast(health.isHealthy ? `✓ ${providerId} is healthy` : `✗ ${providerId} test failed`);
    } catch (error) {
      this.showToast(`Error testing ${providerId}`);
    } finally {
      const testing = new Set(this.state.testingProviders);
      testing.delete(providerId);
      this.setState({ testingProviders: testing });
    }
  }

  async saveAiProvider() {
    if (!this.state.editingProvider) return;
    this.setState({ savingProvider: true });
    
    try {
      const provider = this.state.editingProvider;
      const response = await fetch(`/api/v1/providers/${provider.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(provider)
      });
      
      if (response.ok) {
        const updated = await response.json();
        const providers = this.state.aiProviders;
        if (providers) {
          providers.providers = providers.providers.map(p => p.id === provider.id ? updated : p);
          this.setState({ aiProviders: providers, editingProvider: null });
          this.showToast(`✓ ${provider.displayName} updated`);
        }
      } else {
        this.showToast("Error saving provider configuration");
      }
    } catch (error) {
      this.showToast("Error saving provider configuration");
    } finally {
      this.setState({ savingProvider: false });
    }
  }

  renderAiModels() {
    const providers = this.state.aiProviders?.providers || [];
    const health = this.state.aiProviderHealth || {};
    const editing = this.state.editingProvider;
    const aiProvidersState = this.state.aiProviders;
    const defaultRoute = this.state.aiProviders?.defaultRoute || "offline";
    const errorMessage = this.state.providerLoadError;

    return <div>
      {this.renderTopbar("AI Models & Configuration")}
      <div className="page">
        <section className="hero">
          <div>
            <div className="eyebrow">Provider management</div>
            <h1>AI Model Configuration</h1>
            <p>Configure, test, and manage your AI provider integrations. Switch between local (Ollama), cloud (OpenAI, Anthropic), or mock providers.</p>
          </div>
          <button className="primary" onClick={() => this.loadAiProviders()} style={{display: "flex", alignItems: "center", gap: "6px"}}>↻ Refresh providers</button>
        </section>

        {errorMessage && (
          <div className="panel" style={{marginBottom: "20px", backgroundColor: "#fef2f2", borderColor: "#fca5a5", padding: "16px", borderRadius: "8px", border: "1px solid #fca5a5"}}>
            <div style={{fontSize: "13px", color: "#991b1b"}}>
              <strong style={{display: "block", marginBottom: "6px"}}>✗ Error Loading Providers</strong>
              <span style={{display: "block", marginBottom: "12px", fontFamily: "monospace", fontSize: "12px", backgroundColor: "#fff5f5", padding: "8px", borderRadius: "4px", color: "#7f1d1d"}}>{errorMessage}</span>
              <button 
                onClick={() => this.loadAiProviders()}
                style={{padding: "6px 12px", backgroundColor: "#dc2626", color: "#fff", border: "none", borderRadius: "4px", cursor: "pointer", fontSize: "12px", fontWeight: 600}}
              >
                Retry
              </button>
            </div>
          </div>
        )}

        {providers.length > 0 && (
          <div className="panel" style={{marginBottom: "20px", backgroundColor: "#f0f9ff", borderColor: "#0ea5e9", padding: "16px", borderRadius: "8px", border: "1px solid #0ea5e9"}}>
            <div style={{fontSize: "13px", color: "#0369a1"}}>
              <strong style={{display: "block", marginBottom: "6px"}}>✓ Configuration Loaded</strong>
              <span>{providers.length} provider{providers.length !== 1 ? 's' : ''} available · Default route: <strong>{defaultRoute}</strong></span>
            </div>
          </div>
        )}

        <div className="panel">
          <div className="panel-head">
            <div>
              <div className="panel-title">Active providers</div>
              <div className="panel-sub">Available AI model integrations ({providers.length})</div>
            </div>
          </div>
          
          <div className="provider-grid" style={{display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(300px, 1fr))", gap: "16px", padding: "16px"}}>
            {providers.length === 0 ? (
              <div style={{gridColumn: "1 / -1", padding: "32px 16px", textAlign: "center", color: "#999", backgroundColor: "#f9fafb", borderRadius: "8px"}}>
                <div style={{marginBottom: "12px", fontSize: "32px"}}>⚙️</div>
                <div style={{fontWeight: 600, marginBottom: "4px"}}>
                  {errorMessage ? "Failed to load providers" : "Loading provider configuration..."}
                </div>
                <div style={{fontSize: "13px"}}>
                  {errorMessage 
                    ? "Click the Refresh button above to try again. Make sure the API server is running."
                    : "Click 'Refresh providers' to reload from server"}
                </div>
              </div>
            ) : (
              providers.map(provider => {
                const isEditing = editing?.id === provider.id;
                const providerHealth = health[provider.id];
                const isHealthy = providerHealth?.isHealthy ?? false;
                const isTesting = this.state.testingProviders.has(provider.id);

                return <div key={provider.id} className="provider-card" style={{border: "1px solid #ddd", borderRadius: "8px", padding: "16px", backgroundColor: "#fafbfc"}}>
                  <div style={{display: "flex", justifyContent: "space-between", alignItems: "start", marginBottom: "12px"}}>
                    <div style={{flex: 1}}>
                      <div style={{fontWeight: 600, marginBottom: "4px", fontSize: "14px"}}>{provider.displayName}</div>
                      <div style={{fontSize: "12px", color: "#666", marginBottom: "8px"}}>{provider.type.toUpperCase()} · {provider.model}</div>
                    </div>
                    <div style={{display: "flex", flexDirection: "column", gap: "4px", alignItems: "flex-end"}}>
                      <span style={{
                        padding: "3px 8px",
                        borderRadius: "12px",
                        fontSize: "11px",
                        fontWeight: 600,
                        backgroundColor: provider.enabled ? "#d1fae5" : "#fee2e2",
                        color: provider.enabled ? "#065f46" : "#991b1b",
                        whiteSpace: "nowrap"
                      }}>
                        {provider.enabled ? "✓ ENABLED" : "○ DISABLED"}
                      </span>
                    </div>
                  </div>

                  {providerHealth && (
                    <div style={{marginBottom: "12px", padding: "8px", backgroundColor: isHealthy ? "#ecfdf5" : "#fef2f2", borderRadius: "4px", border: isHealthy ? "1px solid #86efac" : "1px solid #fca5a5"}}>
                      <div style={{fontSize: "12px", color: isHealthy ? "#065f46" : "#991b1b", fontWeight: 500}}>
                        {isHealthy ? "✓ Connection healthy" : "✗ Unable to connect"}
                      </div>
                      {providerHealth.error && <div style={{fontSize: "11px", marginTop: "4px", color: "#666", maxWidth: "100%", wordWrap: "break-word"}}>{providerHealth.error}</div>}
                      {providerHealth.lastChecked && <div style={{fontSize: "11px", marginTop: "4px", color: "#999"}}>Last checked: {new Date(providerHealth.lastChecked).toLocaleString()}</div>}
                    </div>
                  )}

                  {!isEditing ? (
                    <div>
                      <div style={{marginBottom: "12px", fontSize: "13px", lineHeight: "1.6", color: "#555", backgroundColor: "#f9fafb", padding: "8px", borderRadius: "4px"}}>
                        {provider.baseUrl && <div><strong>URL:</strong> <code style={{fontSize: "11px", backgroundColor: "#fff", padding: "2px 4px", borderRadius: "2px", color: "#666"}}>{provider.baseUrl}</code></div>}
                        <div><strong>Model:</strong> {provider.model}</div>
                        <div><strong>Capabilities:</strong> <span style={{fontSize: "12px"}}>{provider.capabilities?.join(", ") || "N/A"}</span></div>
                        {provider.apiKeyEnvironmentVariable && <div><strong>Auth:</strong> {provider.apiKeyEnvironmentVariable}</div>}
                      </div>
                      <div style={{display: "flex", gap: "8px", flexWrap: "wrap"}}>
                        <button 
                          className="text-btn"
                          onClick={() => this.testAiProvider(provider.id)}
                          disabled={isTesting}
                          style={{flex: "1 1 auto", minWidth: "100px", padding: "8px", fontSize: "12px", border: "1px solid #ddd", borderRadius: "4px", cursor: "pointer", backgroundColor: isTesting ? "#f3f4f6" : "#fff", color: "#333"}}
                        >
                          {isTesting ? <span><span className="spinner" style={{display:"inline-block", marginRight:"4px", verticalAlign:"middle"}}></span>Testing…</span> : "↻ Test"}
                        </button>
                        <button 
                          className="text-btn"
                          onClick={() => this.setState({ editingProvider: provider })}
                          style={{flex: "1 1 auto", minWidth: "100px", padding: "8px", fontSize: "12px", border: "1px solid #ddd", borderRadius: "4px", cursor: "pointer", backgroundColor: "#fff", color: "#333"}}
                        >
                          ✎ Configure
                        </button>
                      </div>
                    </div>
                  ) : (
                    <div>
                      <div style={{marginBottom: "12px"}}>
                        <label style={{display: "block", fontSize: "12px", fontWeight: 600, marginBottom: "4px"}}>Provider ID</label>
                        <input type="text" value={editing.id} disabled style={{width: "100%", padding: "6px", borderRadius: "4px", border: "1px solid #ddd", fontSize: "12px", backgroundColor: "#f3f4f6"}} />
                      </div>
                      <div style={{marginBottom: "12px"}}>
                        <label style={{display: "block", fontSize: "12px", fontWeight: 600, marginBottom: "4px"}}>Display Name</label>
                        <input 
                          type="text" 
                          value={editing.displayName}
                          onChange={(e: any) => this.setState({ editingProvider: { ...editing, displayName: e.target.value } })}
                          style={{width: "100%", padding: "6px", borderRadius: "4px", border: "1px solid #ddd", fontSize: "12px"}}
                        />
                      </div>
                      <div style={{marginBottom: "12px"}}>
                        <label style={{display: "block", fontSize: "12px", fontWeight: 600, marginBottom: "4px"}}>Model</label>
                        <input 
                          type="text" 
                          value={editing.model}
                          onChange={(e: any) => this.setState({ editingProvider: { ...editing, model: e.target.value } })}
                          style={{width: "100%", padding: "6px", borderRadius: "4px", border: "1px solid #ddd", fontSize: "12px"}}
                        />
                      </div>
                      <div style={{marginBottom: "12px"}}>
                        <label style={{display: "block", fontSize: "12px", fontWeight: 600, marginBottom: "4px"}}>Base URL</label>
                        <input 
                          type="text" 
                          value={editing.baseUrl || ""}
                          onChange={(e: any) => this.setState({ editingProvider: { ...editing, baseUrl: e.target.value } })}
                          style={{width: "100%", padding: "6px", borderRadius: "4px", border: "1px solid #ddd", fontSize: "12px"}}
                        />
                      </div>
                      <div style={{marginBottom: "12px"}}>
                        <label style={{display: "block", fontSize: "12px", fontWeight: 600, marginBottom: "4px"}}>
                          <input 
                            type="checkbox" 
                            checked={editing.enabled}
                            onChange={(e: any) => this.setState({ editingProvider: { ...editing, enabled: e.target.checked } })}
                            style={{marginRight: "6px"}}
                          />
                          Enabled
                        </label>
                      </div>
                      <div style={{display: "flex", gap: "8px"}}>
                        <button 
                          className="success"
                          onClick={() => this.saveAiProvider()}
                          disabled={this.state.savingProvider}
                          style={{flex: 1, padding: "8px", fontSize: "12px", border: "none", borderRadius: "4px", cursor: "pointer", backgroundColor: "#059669", color: "#fff"}}
                        >
                          {this.state.savingProvider ? "Saving…" : "✓ Save changes"}
                        </button>
                        <button 
                          className="text-btn"
                          onClick={() => this.setState({ editingProvider: null })}
                          style={{flex: 1, padding: "8px", fontSize: "12px", border: "1px solid #ddd", borderRadius: "4px", cursor: "pointer", backgroundColor: "#fff"}}
                        >
                          Cancel
                        </button>
                      </div>
                    </div>
                  )}
                </div>;
              })
            )}
          </div>
        </div>

        {aiProvidersState?.routes && aiProvidersState.routes.length > 0 && (
          <div className="panel" style={{marginTop: "20px"}}>
            <div className="panel-head">
              <div>
                <div className="panel-title">Routing configuration</div>
                <div className="panel-sub">Task-to-provider routing rules</div>
              </div>
            </div>
            <div style={{padding: "16px"}}>
              {aiProvidersState.routes.map(route => (
                <div key={route.id} style={{marginBottom: "16px", padding: "12px", backgroundColor: defaultRoute === route.id ? "#f0fdf4" : "#f9fafb", borderRadius: "6px", border: defaultRoute === route.id ? "1px solid #86efac" : "1px solid #e5e7eb"}}>
                  <div style={{fontWeight: 600, marginBottom: "12px", color: defaultRoute === route.id ? "#059669" : "#333", display: "flex", alignItems: "center", gap: "8px"}}>
                    {route.id}
                    {defaultRoute === route.id && <span style={{fontSize: "11px", padding: "2px 8px", backgroundColor: "#d1fae5", borderRadius: "4px", color: "#065f46", fontWeight: 600}}>DEFAULT ROUTE</span>}
                  </div>
                  {Object.entries(route.tasks).map(([task, providerList]) => (
                    <div key={task} style={{marginBottom: "8px", fontSize: "13px", color: "#555"}}>
                      <span style={{display: "inline-block", minWidth: "80px", fontWeight: 500, color: "#333"}}>{task}:</span>
                      <span style={{fontFamily: "monospace", fontSize: "12px", backgroundColor: "#fff", padding: "2px 6px", borderRadius: "3px", color: "#666"}}>
                        {(providerList as string[]).map((p, i) => (
                          <span key={p}>
                            {i > 0 && <span style={{margin: "0 4px"}}>→</span>}
                            <span style={{color: i === 0 ? "#059669" : "#666", fontWeight: i === 0 ? 600 : 400}}>{p}</span>
                          </span>
                        ))}
                      </span>
                    </div>
                  ))}
                </div>
              ))}
            </div>
            <div style={{padding: "16px", backgroundColor: "#fffbeb", borderRadius: "6px", border: "1px solid #fcd34d", fontSize: "13px"}}>
              <span style={{fontWeight: 600}}>💡 Provider fallback order:</span> Tasks use the first available provider in order. If the primary provider fails or is disabled, the next provider in the chain handles the task.
            </div>
          </div>
        )}
      </div>
    </div>;
  }

  render() {
    let content: any;
    switch (this.state.view) {
      case "drafts": content = this.renderDraft(); break;
      case "sources": content = this.renderSources(); break;
      case "ai-models": content = this.renderAiModels(); break;
      case "signals": content = this.renderPlaceholder("Content signals", "Search, filter, score, promote, and archive every collected engineering signal."); break;
      case "topics": content = this.renderPlaceholder("Topic map", "Tune the weighted taxonomy across .NET, AI, system design, cloud, DevOps, leadership, performance, security, and career growth."); break;
      case "settings": content = this.renderPlaceholder("Workspace settings", "Configure your profile voice, scheduler, JSON storage mode, provider routes, and review rules."); break;
      default: content = this.renderOverview();
    }
    return <div className="shell">{this.renderSidebar()}<main className="main">{content}</main>{this.state.toast ? <div className="toast"><span>✓</span>{this.state.toast}</div> : null}</div>;
  }
}

ReactDOM.render(<App />, document.getElementById("root"));
