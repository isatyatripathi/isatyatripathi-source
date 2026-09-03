const navItems = [
    { id: "overview", label: "Overview", icon: "◫" },
    { id: "signals", label: "Content signals", icon: "⌁" },
    { id: "drafts", label: "Review queue", icon: "✎" },
    { id: "sources", label: "Data sources", icon: "◎" },
    { id: "topics", label: "Topic map", icon: "◇" },
    { id: "ai-models", label: "AI Models", icon: "🤖" },
    { id: "settings", label: "Settings", icon: "⚙" }
];
const pipelineLabels = ["Collect", "Normalize", "Deduplicate", "Score", "Draft", "Review"];
class App extends React.Component {
    constructor(props) {
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
                    providersResponse.health.forEach((health) => {
                        aiProviderHealth[health.providerId] = health;
                    });
                }
            }
            else if (providersResponse.providers) {
                aiProviders = providersResponse;
                aiProviderHealth = {};
            }
            else if (providersResponse.schemaVersion !== undefined) {
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
        }
        catch (error) {
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
    showToast(message) {
        if (this.toastTimer)
            window.clearTimeout(this.toastTimer);
        this.setState({ toast: message });
        this.toastTimer = window.setTimeout(() => this.setState({ toast: "" }), 2800);
    }
    setView(view) {
        this.setState({ view });
        // Load AI providers data when switching to that view
        if (view === "ai-models" && !this.state.aiProviders) {
            this.loadAiProviders();
        }
        window.scrollTo({ top: 0, behavior: "smooth" });
    }
    async runScan() {
        if (this.state.scanRunning)
            return;
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
    async generateDraft(itemId) {
        var _a;
        if (this.state.generating)
            return;
        this.setState({ generating: true });
        const configuredProviders = ((_a = this.state.aiProviders) === null || _a === void 0 ? void 0 : _a.providers) || [];
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
            if (failedResponse)
                throw new Error(`Draft generation failed (${failedResponse.status})`);
            await new Promise(resolve => window.setTimeout(resolve, 900));
            const draftResponse = await fetch("/api/v1/drafts/demo-draft");
            if (!draftResponse.ok)
                throw new Error(`Could not load generated draft (${draftResponse.status})`);
            const draft = await draftResponse.json();
            this.setState({ draft, view: "drafts" });
            const providerNames = providerRequests.map(providerId => { var _a; return ((_a = configuredProviders.find(provider => provider.id === providerId)) === null || _a === void 0 ? void 0 : _a.displayName) || providerId; });
            this.showToast(`Draft generated using ${providerNames.join(", ")} in parallel with a safe Mermaid diagram and source mapping.`);
            window.scrollTo({ top: 0, behavior: "smooth" });
        }
        catch (error) {
            this.showToast(error instanceof Error ? error.message : "Draft generation failed");
        }
        finally {
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
        if (!draft)
            return;
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
        return React.createElement("aside", { className: "sidebar" },
            React.createElement("div", { className: "brand" },
                React.createElement("div", { className: "brand-mark" }, "D"),
                React.createElement("div", null,
                    React.createElement("div", { className: "brand-name" }, "DevSignal Studio"),
                    React.createElement("div", { className: "brand-sub" }, "AI-powered content intelligence"))),
            React.createElement("div", { className: "nav-title" }, "Workspace"),
            React.createElement("nav", { className: "nav" }, navItems.map(item => React.createElement("button", { key: item.id, "data-nav": item.id, className: `nav-btn ${this.state.view === item.id ? "active" : ""}`, onClick: () => this.setView(item.id) },
                React.createElement("span", { className: "nav-icon" }, item.icon),
                React.createElement("span", { className: "nav-label" }, item.label)))),
            React.createElement("div", { className: "spacer" }),
            React.createElement("div", { className: "local-box" },
                React.createElement("div", { className: "local-line" },
                    React.createElement("span", { className: "pulse" }),
                    "Local workspace online"),
                React.createElement("p", null,
                    "Offline AI route \u00B7 JSON snapshots",
                    React.createElement("br", null),
                    "No automatic publishing")),
            React.createElement("div", { className: "user-card" },
                React.createElement("div", { className: "avatar" }, "SA"),
                React.createElement("div", null,
                    React.createElement("div", { className: "user-name" }, "Satya"),
                    React.createElement("div", { className: "user-role" }, "Senior Software Engineering Lead"))));
    }
    renderTopbar(section) {
        var _a;
        const selectedProviderDisplay = this.state.selectedProviderIds.includes("all")
            ? "All enabled models"
            : this.state.selectedProviderIds.map(id => { var _a, _b; return ((_b = (_a = this.state.aiProviders) === null || _a === void 0 ? void 0 : _a.providers.find(provider => provider.id === id)) === null || _b === void 0 ? void 0 : _b.displayName) || id; }).join(", ") || "No provider selected";
        return React.createElement("header", { className: "topbar" },
            React.createElement("div", { className: "crumb" },
                React.createElement("span", null, "Workspace"),
                React.createElement("span", null, "/"),
                React.createElement("strong", null, section)),
            React.createElement("div", { className: "top-actions" },
                React.createElement("div", { style: { display: "flex", gap: "12px", alignItems: "center" } },
                    React.createElement("div", { style: { display: "flex", alignItems: "center", gap: "8px", padding: "6px 12px", backgroundColor: "#f3f4f6", borderRadius: "6px", fontSize: "12px" } },
                        React.createElement("span", { style: { fontWeight: 500 } }, "Using:"),
                        React.createElement("select", { multiple: true, value: this.state.selectedProviderIds, onChange: (e) => {
                                const values = Array.from(e.target.selectedOptions).map(option => option.value);
                                this.setState({ selectedProviderIds: values.length > 0 ? values : ["mock"] });
                            }, style: {
                                padding: "4px 8px",
                                borderRadius: "4px",
                                border: "1px solid #d1d5db",
                                backgroundColor: "#fff",
                                fontSize: "12px",
                                fontWeight: 600,
                                cursor: "pointer",
                                color: "#059669"
                            } },
                            React.createElement("option", { value: "all" }, "All enabled models (parallel)"),
                            (((_a = this.state.aiProviders) === null || _a === void 0 ? void 0 : _a.providers) || []).map(provider => (React.createElement("option", { key: provider.id, value: provider.id, disabled: !provider.enabled },
                                provider.displayName,
                                " ",
                                provider.enabled ? "" : "(disabled)"))))),
                    React.createElement("button", { className: "icon-btn", "aria-label": "Notifications" }, "\u25CC"),
                    React.createElement("button", { className: "icon-btn", "aria-label": "Help" }, "?"))));
    }
    renderStats() {
        const d = this.state.dashboard;
        const cards = [
            ["Signals collected", d.signals, "⌁", "3 added by the latest run", true],
            ["Draft candidates", d.candidates, "✦", "Minimum relevance score: 70", true],
            ["Review queue", d.drafts, "✎", "Human approval required", false],
            ["Active sources", `${d.activeSources}/${d.totalSources}`, "◎", "Configurable connectors", false]
        ];
        return React.createElement("div", { className: "stats" }, cards.map((card, index) => React.createElement("div", { className: "stat", key: index },
            React.createElement("div", { className: "stat-top" },
                React.createElement("span", null, card[0]),
                React.createElement("span", { className: "stat-icon" }, card[2])),
            React.createElement("div", { className: "stat-value" }, card[1]),
            React.createElement("div", { className: `stat-foot ${card[4] ? "good" : ""}` },
                card[4] ? "↑ " : "",
                card[3]))));
    }
    renderPipeline() {
        const phase = this.state.scanPhase;
        const fill = phase <= 0 ? 0 : Math.min(86, ((phase - 1) / 5) * 86);
        return React.createElement("div", { className: "panel" },
            React.createElement("div", { className: "panel-head" },
                React.createElement("div", null,
                    React.createElement("div", { className: "panel-title" }, "Daily intelligence pipeline"),
                    React.createElement("div", { className: "panel-sub" }, "Filter before AI \u00B7 preserve provenance \u00B7 review before publishing")),
                React.createElement("button", { className: "text-btn" }, "Run history \u2192")),
            React.createElement("div", { className: "pipeline" },
                React.createElement("div", { className: "pipeline-track" },
                    React.createElement("div", { className: "pipeline-fill", style: { width: `${fill}%` } }),
                    pipelineLabels.map((label, i) => {
                        const step = i + 1;
                        const cls = phase > step ? "done" : phase === step ? "active" : "";
                        return React.createElement("div", { className: `step ${cls}`, key: label },
                            React.createElement("div", { className: "step-dot" }, phase > step ? "✓" : step),
                            React.createElement("div", { className: "step-label" }, label));
                    })),
                React.createElement("div", { className: "pipeline-summary" },
                    React.createElement("span", null, this.state.scanRunning ? React.createElement("span", null,
                        React.createElement("span", { className: "spinner", style: { display: "inline-block", marginRight: "7px", verticalAlign: "middle" } }),
                        "Processing local source\u2026") : React.createElement("span", null,
                        "Last completed ",
                        React.createElement("strong", null, this.state.dashboard.lastRun))),
                    React.createElement("span", null,
                        React.createElement("strong", null, "3"),
                        " collected \u00B7 ",
                        React.createElement("strong", null, "3"),
                        " selected \u00B7 ",
                        React.createElement("strong", null, "0"),
                        " duplicates"))));
    }
    renderSignals() {
        return React.createElement("div", { className: "panel", style: { marginTop: "15px" } },
            React.createElement("div", { className: "panel-head" },
                React.createElement("div", null,
                    React.createElement("div", { className: "panel-title" }, "Top signals for your profile"),
                    React.createElement("div", { className: "panel-sub" }, "Ranked for full-stack AI-powered .NET engineering")),
                React.createElement("button", { className: "text-btn", onClick: () => this.setView("signals") }, "View all \u2192")),
            React.createElement("div", { className: "signals" }, this.state.items.map(item => React.createElement("div", { className: "signal", key: item.id },
                React.createElement("div", { className: "score", style: { "--score": item.score } },
                    React.createElement("span", null, item.score)),
                React.createElement("div", null,
                    React.createElement("div", { className: "signal-source" },
                        React.createElement("span", { className: "source-dot" }),
                        item.sourceName,
                        " \u00B7 ",
                        item.age),
                    React.createElement("h3", { className: "signal-title" }, item.title),
                    React.createElement("p", { className: "signal-summary" }, item.summary),
                    React.createElement("div", { className: "tags" }, item.tags.slice(0, 4).map(tag => React.createElement("span", { className: "tag", key: tag }, tag)))),
                React.createElement("button", { className: "draft-btn", "data-action": `draft-${item.id}`, onClick: () => this.generateDraft(item.id) }, this.state.generating ? "Generating…" : "Create draft →")))));
    }
    renderOverview() {
        const draft = this.state.draft;
        const activity = [32, 46, 38, 64, 50, 76, 92];
        return React.createElement("div", null,
            this.renderTopbar("Overview"),
            React.createElement("div", { className: "page" },
                React.createElement("section", { className: "hero" },
                    React.createElement("div", null,
                        React.createElement("div", { className: "eyebrow" }, "Monday intelligence brief"),
                        React.createElement("h1", null, "Good morning, Satya."),
                        React.createElement("p", null, "Your local sandbox found three strong ideas connecting AI engineering, .NET architecture, security, and senior-level communication.")),
                    React.createElement("button", { className: "primary", "data-action": "run-scan", disabled: this.state.scanRunning, onClick: () => this.runScan() },
                        this.state.scanRunning ? React.createElement("span", { className: "spinner" }) : "✦",
                        this.state.scanRunning ? "Scanning sources…" : "Run daily scan")),
                this.renderStats(),
                React.createElement("div", { className: "grid" },
                    React.createElement("div", null,
                        this.renderPipeline(),
                        this.renderSignals()),
                    React.createElement("div", { className: "side" },
                        React.createElement("div", { className: "panel review" },
                            React.createElement("div", { className: "review-top" },
                                React.createElement("div", null,
                                    React.createElement("div", { className: "panel-title" }, "Next to review"),
                                    React.createElement("div", { className: "panel-sub" }, "LinkedIn practical explainer")),
                                React.createElement("span", { className: `status ${draft && draft.status === "approved" ? "approved" : "reviewing"}` }, draft && draft.status === "approved" ? "Approved" : "In review")),
                            React.createElement("div", { className: "review-title" }, draft ? draft.title : "AI-provider abstraction in ASP.NET Core"),
                            React.createElement("p", { className: "review-copy" }, "A practical draft about keeping providers replaceable without erasing useful model capabilities."),
                            React.createElement("div", { className: "review-meta" },
                                React.createElement("span", null,
                                    draft ? draft.characterCount : 1248,
                                    " chars \u00B7 Mermaid included"),
                                React.createElement("button", { className: "review-link", "data-action": "open-review", onClick: () => this.setView("drafts") }, "Open review \u2192"))),
                        React.createElement("div", { className: "panel" },
                            React.createElement("div", { className: "panel-head" },
                                React.createElement("div", null,
                                    React.createElement("div", { className: "panel-title" }, "Topic coverage"),
                                    React.createElement("div", { className: "panel-sub" }, "Latest seven days"))),
                            React.createElement("div", { className: "coverage" },
                                React.createElement("div", { className: "donut" },
                                    React.createElement("div", { className: "donut-text" },
                                        React.createElement("div", { className: "donut-value" }, "12"),
                                        React.createElement("div", { className: "donut-label" }, "active topics"))),
                                React.createElement("div", { className: "legend" }, [["#8b5cf6", "AI + .NET", "38%"], ["#22d3ee", "System design", "23%"], ["#34d399", "Leadership", "17%"], ["#fbbf24", "Cloud + DevOps", "13%"], ["#fb7185", "Security", "9%"]].map((x) => React.createElement("div", { className: "legend-row", key: x[1] },
                                    React.createElement("span", { className: "legend-dot", style: { background: x[0] } }),
                                    React.createElement("span", null, x[1]),
                                    React.createElement("strong", null, x[2])))))),
                        React.createElement("div", { className: "panel" },
                            React.createElement("div", { className: "panel-head" },
                                React.createElement("div", null,
                                    React.createElement("div", { className: "panel-title" }, "Content momentum"),
                                    React.createElement("div", { className: "panel-sub" }, "Signals selected per day"))),
                            React.createElement("div", { className: "activity" },
                                React.createElement("div", { className: "bars" }, activity.map((h, i) => React.createElement("div", { className: "bar-wrap", key: i },
                                    React.createElement("div", { className: "bar", style: { height: `${h}%` } }),
                                    React.createElement("div", { className: "bar-label" }, ["T", "W", "T", "F", "S", "S", "M"][i]))))))))));
    }
    renderDiagram() {
        return React.createElement("div", { className: "diagram" },
            React.createElement("svg", { viewBox: "0 0 520 292", role: "img", "aria-label": "AI provider routing workflow" },
                React.createElement("defs", null,
                    React.createElement("marker", { id: "arrow", markerWidth: "7", markerHeight: "7", refX: "5", refY: "3.5", orient: "auto" },
                        React.createElement("path", { d: "M0,0 L0,7 L7,3.5 z", fill: "#5b687d" }))),
                React.createElement("path", { className: "edge", d: "M112 145 L174 145" }),
                React.createElement("path", { className: "edge", d: "M262 145 C292 145 288 58 320 58" }),
                React.createElement("path", { className: "edge", d: "M262 145 L320 145" }),
                React.createElement("path", { className: "edge", d: "M262 145 C292 145 288 232 320 232" }),
                React.createElement("path", { className: "edge", d: "M398 58 C438 58 430 112 452 128" }),
                React.createElement("path", { className: "edge", d: "M398 145 L452 145" }),
                React.createElement("path", { className: "edge", d: "M398 232 C438 232 430 177 452 162" }),
                React.createElement("rect", { className: "node", x: "28", y: "119", rx: "9", width: "84", height: "52" }),
                React.createElement("text", { className: "node-text", x: "70", y: "140" }, "Content"),
                React.createElement("text", { className: "node-text", x: "70", y: "154" }, "task"),
                React.createElement("rect", { className: "node", x: "174", y: "119", rx: "9", width: "88", height: "52" }),
                React.createElement("text", { className: "node-text", x: "218", y: "140" }, "Route by"),
                React.createElement("text", { className: "node-text", x: "218", y: "154" }, "capability"),
                React.createElement("rect", { className: "node alt", x: "320", y: "38", rx: "9", width: "78", height: "40" }),
                React.createElement("text", { className: "node-text", x: "359", y: "62" }, "Ollama"),
                React.createElement("rect", { className: "node alt", x: "320", y: "125", rx: "9", width: "78", height: "40" }),
                React.createElement("text", { className: "node-text", x: "359", y: "149" }, "OpenAI"),
                React.createElement("rect", { className: "node alt", x: "320", y: "212", rx: "9", width: "78", height: "40" }),
                React.createElement("text", { className: "node-text", x: "359", y: "236" }, "Anthropic"),
                React.createElement("rect", { className: "node good", x: "452", y: "119", rx: "9", width: "48", height: "52" }),
                React.createElement("text", { className: "node-text", x: "476", y: "140" }, "Validate"),
                React.createElement("text", { className: "node-text", x: "476", y: "154" }, "result")));
    }
    renderDraft() {
        const d = this.state.draft;
        if (!d)
            return React.createElement("div", null,
                this.renderTopbar("Review queue"),
                React.createElement("div", { className: "page" },
                    React.createElement("section", { className: "hero" },
                        React.createElement("div", null,
                            React.createElement("h1", null, "Preparing draft\u2026")))));
        const approved = d.status === "approved";
        return React.createElement("div", null,
            this.renderTopbar("Review queue / Draft"),
            React.createElement("div", { className: "page" },
                React.createElement("div", { className: "draft-header" },
                    React.createElement("div", { className: "draft-heading" },
                        React.createElement("button", { className: "back", "data-action": "back", onClick: () => this.setView("overview") }, "\u2190"),
                        React.createElement("div", null,
                            React.createElement("h1", null, "Review LinkedIn draft"),
                            React.createElement("p", null, "Generated locally \u00B7 deterministic provider \u00B7 source-backed \u00B7 revision 1"))),
                    React.createElement("div", { className: "draft-actions" },
                        React.createElement("span", { className: `status ${approved ? "approved" : "reviewing"}` }, approved ? "Approved" : "In review"),
                        React.createElement("button", { className: "secondary", onClick: () => this.exportDraft() }, "\u21E9 Export"),
                        React.createElement("button", { className: "success", "data-action": "approve", onClick: () => this.approveDraft() }, approved ? "✓ Approved" : "✓ Approve draft"))),
                React.createElement("div", { className: "draft-grid" },
                    React.createElement("div", { className: "panel" },
                        React.createElement("div", { className: "editor-toolbar" },
                            React.createElement("div", { className: "tabs" },
                                React.createElement("button", { className: "tab active" }, "Post preview"),
                                React.createElement("button", { className: "tab" }, "Edit content"),
                                React.createElement("button", { className: "tab" }, "Claims")),
                            React.createElement("div", { className: "char" },
                                d.characterCount,
                                " / 2,800 characters")),
                        React.createElement("div", { className: "post-wrap" },
                            React.createElement("div", { className: "linkedin" },
                                React.createElement("div", { className: "li-head" },
                                    React.createElement("div", { className: "li-avatar" }, "SA"),
                                    React.createElement("div", null,
                                        React.createElement("div", { className: "li-name" }, "Satya"),
                                        React.createElement("div", { className: "li-role" }, "Senior Software Engineering Lead \u00B7 Just now \u00B7 \u25C9"))),
                                React.createElement("div", { className: "li-body" },
                                    React.createElement("div", { className: "li-hook" }, d.hook),
                                    "\n\n",
                                    d.body,
                                    React.createElement("div", { className: "li-tags" }, d.hashtags.join(" "))),
                                React.createElement("div", { className: "li-footer" },
                                    React.createElement("span", null, "\u2661 Like"),
                                    React.createElement("span", null, "\u25EF Comment"),
                                    React.createElement("span", null, "\u2197 Repost"),
                                    React.createElement("span", null, "\u2708 Send"))))),
                    React.createElement("div", { className: "inspector" },
                        React.createElement("div", { className: "panel" },
                            React.createElement("div", { className: "panel-head" },
                                React.createElement("div", null,
                                    React.createElement("div", { className: "panel-title" }, "Architecture diagram"),
                                    React.createElement("div", { className: "panel-sub" }, "Editable Mermaid \u00B7 safe rendering mode")),
                                React.createElement("span", { className: "status approved" }, "Validated")),
                            React.createElement("div", { className: "diagram-stage" }, this.renderDiagram()),
                            React.createElement("pre", { className: "code" }, d.mermaid)),
                        React.createElement("div", { className: "panel" },
                            React.createElement("div", { className: "panel-head" },
                                React.createElement("div", null,
                                    React.createElement("div", { className: "panel-title" }, "Quality gates"),
                                    React.createElement("div", { className: "panel-sub" }, "Human review remains the final control")),
                                React.createElement("span", { className: "status approved" }, "4 passed")),
                            React.createElement("div", { className: "validation" }, [["Length and format", "Within LinkedIn recipe limit", "PASS"], ["Claims and sources", "No unsupported external claims", "PASS"], ["Diagram safety", "No links, scripts, or click handlers", "PASS"], ["Voice alignment", "Practical, clear, evidence-aware", "PASS"]].map((x) => React.createElement("div", { className: "check-row", key: x[0] },
                                React.createElement("span", { className: "check" }, "\u2713"),
                                React.createElement("div", null,
                                    React.createElement("div", { className: "check-title" }, x[0]),
                                    React.createElement("div", { className: "check-note" }, x[1])),
                                React.createElement("span", { className: "check-value" }, x[2])))),
                            React.createElement("div", { className: "source-ref" },
                                React.createElement("label", null, "Primary source"),
                                React.createElement("strong", null, d.sourceTitle),
                                React.createElement("span", null,
                                    d.sourceName,
                                    " \u00B7 local curated JSON \u00B7 provenance retained")))))));
    }
    renderSources() {
        return React.createElement("div", null,
            this.renderTopbar("Data sources"),
            React.createElement("div", { className: "page" },
                React.createElement("section", { className: "hero" },
                    React.createElement("div", null,
                        React.createElement("div", { className: "eyebrow" }, "Configurable connectors"),
                        React.createElement("h1", null, "Source catalog"),
                        React.createElement("p", null, "Combine official RSS feeds, Stack Exchange, local JSON, remote JSON, and explicit manual capture without depending on one AI provider.")),
                    React.createElement("button", { className: "primary" }, "\uFF0B Add source")),
                React.createElement("div", { className: "source-grid" }, this.state.sources.map(source => React.createElement("div", { className: "source-card", key: source.id },
                    React.createElement("div", { className: "source-logo" }, source.name.substring(0, 2).toUpperCase()),
                    React.createElement("div", null,
                        React.createElement("div", { className: "source-name" }, source.name),
                        React.createElement("div", { className: "source-meta" },
                            source.connectorType,
                            " \u00B7 trust ",
                            Math.round(source.trustWeight * 100),
                            "% \u00B7 daily")),
                    React.createElement("div", { className: `health ${source.enabled ? "" : "off"}` }, source.enabled ? "● ENABLED" : "○ DISABLED"))))));
    }
    renderPlaceholder(title, subtitle) {
        return React.createElement("div", null,
            this.renderTopbar(title),
            React.createElement("div", { className: "page" },
                React.createElement("section", { className: "hero" },
                    React.createElement("div", null,
                        React.createElement("div", { className: "eyebrow" }, "DevSignal Studio"),
                        React.createElement("h1", null, title),
                        React.createElement("p", null, subtitle))),
                this.renderSignals()));
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
            let data;
            try {
                data = await response.json();
            }
            catch (parseError) {
                const text = await response.clone().text();
                console.error("Failed to parse JSON:", parseError, "Response text:", text.substring(0, 300));
                throw new Error(`Invalid JSON response: ${text.substring(0, 150)}`);
            }
            let aiProviders = null;
            let aiProviderHealth = {};
            if (data.configuration) {
                aiProviders = data.configuration;
            }
            else if (data.providers) {
                aiProviders = data;
            }
            else if (data.schemaVersion !== undefined) {
                aiProviders = data;
            }
            else {
                throw new Error("API returned data in unexpected format");
            }
            if (Array.isArray(data.health)) {
                data.health.forEach((health) => {
                    aiProviderHealth[health.providerId] = health;
                });
            }
            // Set default provider if not already set
            let defaultProviders = this.state.selectedProviderIds;
            if (!defaultProviders.some(id => aiProviders === null || aiProviders === void 0 ? void 0 : aiProviders.providers.some(provider => provider.id === id))) {
                const enabledProvider = aiProviders === null || aiProviders === void 0 ? void 0 : aiProviders.providers.find(p => p.enabled);
                defaultProviders = [(enabledProvider === null || enabledProvider === void 0 ? void 0 : enabledProvider.id) || "mock"];
            }
            this.setState({ aiProviders, aiProviderHealth, selectedProviderIds: defaultProviders });
        }
        catch (error) {
            const errorMessage = error instanceof Error ? error.message : "Failed to load AI providers";
            console.error("Error loading AI providers:", error);
            this.setState({
                providerLoadError: errorMessage,
                aiProviders: { schemaVersion: 1, defaultRoute: "offline", providers: [], routes: [] }
            });
            this.showToast("Error loading provider configuration: " + errorMessage);
        }
    }
    async testAiProvider(providerId) {
        var _a, _b, _c;
        const testing = new Set(this.state.testingProviders);
        testing.add(providerId);
        this.setState({ testingProviders: testing });
        try {
            const response = await fetch(`/api/v1/providers/${providerId}/test`, { method: "POST" });
            if (!response.ok)
                throw new Error(`Provider test failed (${response.status})`);
            const result = await response.json();
            const health = {
                ...result,
                isHealthy: (_a = result.isHealthy) !== null && _a !== void 0 ? _a : result.status === "healthy",
                lastChecked: (_b = result.lastChecked) !== null && _b !== void 0 ? _b : result.checkedAt,
                error: (_c = result.error) !== null && _c !== void 0 ? _c : result.message
            };
            const healthMap = this.state.aiProviderHealth || {};
            healthMap[providerId] = health;
            this.setState({ aiProviderHealth: healthMap });
            this.showToast(health.isHealthy ? `✓ ${providerId} is healthy` : `✗ ${providerId} test failed`);
        }
        catch (error) {
            this.showToast(`Error testing ${providerId}`);
        }
        finally {
            const testing = new Set(this.state.testingProviders);
            testing.delete(providerId);
            this.setState({ testingProviders: testing });
        }
    }
    async saveAiProvider() {
        if (!this.state.editingProvider)
            return;
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
            }
            else {
                this.showToast("Error saving provider configuration");
            }
        }
        catch (error) {
            this.showToast("Error saving provider configuration");
        }
        finally {
            this.setState({ savingProvider: false });
        }
    }
    renderAiModels() {
        var _a, _b;
        const providers = ((_a = this.state.aiProviders) === null || _a === void 0 ? void 0 : _a.providers) || [];
        const health = this.state.aiProviderHealth || {};
        const editing = this.state.editingProvider;
        const aiProvidersState = this.state.aiProviders;
        const defaultRoute = ((_b = this.state.aiProviders) === null || _b === void 0 ? void 0 : _b.defaultRoute) || "offline";
        const errorMessage = this.state.providerLoadError;
        return React.createElement("div", null,
            this.renderTopbar("AI Models & Configuration"),
            React.createElement("div", { className: "page" },
                React.createElement("section", { className: "hero" },
                    React.createElement("div", null,
                        React.createElement("div", { className: "eyebrow" }, "Provider management"),
                        React.createElement("h1", null, "AI Model Configuration"),
                        React.createElement("p", null, "Configure, test, and manage your AI provider integrations. Switch between local (Ollama), cloud (OpenAI, Anthropic), or mock providers.")),
                    React.createElement("button", { className: "primary", onClick: () => this.loadAiProviders(), style: { display: "flex", alignItems: "center", gap: "6px" } }, "\u21BB Refresh providers")),
                errorMessage && (React.createElement("div", { className: "panel", style: { marginBottom: "20px", backgroundColor: "#fef2f2", borderColor: "#fca5a5", padding: "16px", borderRadius: "8px", border: "1px solid #fca5a5" } },
                    React.createElement("div", { style: { fontSize: "13px", color: "#991b1b" } },
                        React.createElement("strong", { style: { display: "block", marginBottom: "6px" } }, "\u2717 Error Loading Providers"),
                        React.createElement("span", { style: { display: "block", marginBottom: "12px", fontFamily: "monospace", fontSize: "12px", backgroundColor: "#fff5f5", padding: "8px", borderRadius: "4px", color: "#7f1d1d" } }, errorMessage),
                        React.createElement("button", { onClick: () => this.loadAiProviders(), style: { padding: "6px 12px", backgroundColor: "#dc2626", color: "#fff", border: "none", borderRadius: "4px", cursor: "pointer", fontSize: "12px", fontWeight: 600 } }, "Retry")))),
                providers.length > 0 && (React.createElement("div", { className: "panel", style: { marginBottom: "20px", backgroundColor: "#f0f9ff", borderColor: "#0ea5e9", padding: "16px", borderRadius: "8px", border: "1px solid #0ea5e9" } },
                    React.createElement("div", { style: { fontSize: "13px", color: "#0369a1" } },
                        React.createElement("strong", { style: { display: "block", marginBottom: "6px" } }, "\u2713 Configuration Loaded"),
                        React.createElement("span", null,
                            providers.length,
                            " provider",
                            providers.length !== 1 ? 's' : '',
                            " available \u00B7 Default route: ",
                            React.createElement("strong", null, defaultRoute))))),
                React.createElement("div", { className: "panel" },
                    React.createElement("div", { className: "panel-head" },
                        React.createElement("div", null,
                            React.createElement("div", { className: "panel-title" }, "Active providers"),
                            React.createElement("div", { className: "panel-sub" },
                                "Available AI model integrations (",
                                providers.length,
                                ")"))),
                    React.createElement("div", { className: "provider-grid", style: { display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(300px, 1fr))", gap: "16px", padding: "16px" } }, providers.length === 0 ? (React.createElement("div", { style: { gridColumn: "1 / -1", padding: "32px 16px", textAlign: "center", color: "#999", backgroundColor: "#f9fafb", borderRadius: "8px" } },
                        React.createElement("div", { style: { marginBottom: "12px", fontSize: "32px" } }, "\u2699\uFE0F"),
                        React.createElement("div", { style: { fontWeight: 600, marginBottom: "4px" } }, errorMessage ? "Failed to load providers" : "Loading provider configuration..."),
                        React.createElement("div", { style: { fontSize: "13px" } }, errorMessage
                            ? "Click the Refresh button above to try again. Make sure the API server is running."
                            : "Click 'Refresh providers' to reload from server"))) : (providers.map(provider => {
                        var _a, _b;
                        const isEditing = (editing === null || editing === void 0 ? void 0 : editing.id) === provider.id;
                        const providerHealth = health[provider.id];
                        const isHealthy = (_a = providerHealth === null || providerHealth === void 0 ? void 0 : providerHealth.isHealthy) !== null && _a !== void 0 ? _a : false;
                        const isTesting = this.state.testingProviders.has(provider.id);
                        return React.createElement("div", { key: provider.id, className: "provider-card", style: { border: "1px solid #ddd", borderRadius: "8px", padding: "16px", backgroundColor: "#fafbfc" } },
                            React.createElement("div", { style: { display: "flex", justifyContent: "space-between", alignItems: "start", marginBottom: "12px" } },
                                React.createElement("div", { style: { flex: 1 } },
                                    React.createElement("div", { style: { fontWeight: 600, marginBottom: "4px", fontSize: "14px" } }, provider.displayName),
                                    React.createElement("div", { style: { fontSize: "12px", color: "#666", marginBottom: "8px" } },
                                        provider.type.toUpperCase(),
                                        " \u00B7 ",
                                        provider.model)),
                                React.createElement("div", { style: { display: "flex", flexDirection: "column", gap: "4px", alignItems: "flex-end" } },
                                    React.createElement("span", { style: {
                                            padding: "3px 8px",
                                            borderRadius: "12px",
                                            fontSize: "11px",
                                            fontWeight: 600,
                                            backgroundColor: provider.enabled ? "#d1fae5" : "#fee2e2",
                                            color: provider.enabled ? "#065f46" : "#991b1b",
                                            whiteSpace: "nowrap"
                                        } }, provider.enabled ? "✓ ENABLED" : "○ DISABLED"))),
                            providerHealth && (React.createElement("div", { style: { marginBottom: "12px", padding: "8px", backgroundColor: isHealthy ? "#ecfdf5" : "#fef2f2", borderRadius: "4px", border: isHealthy ? "1px solid #86efac" : "1px solid #fca5a5" } },
                                React.createElement("div", { style: { fontSize: "12px", color: isHealthy ? "#065f46" : "#991b1b", fontWeight: 500 } }, isHealthy ? "✓ Connection healthy" : "✗ Unable to connect"),
                                providerHealth.error && React.createElement("div", { style: { fontSize: "11px", marginTop: "4px", color: "#666", maxWidth: "100%", wordWrap: "break-word" } }, providerHealth.error),
                                providerHealth.lastChecked && React.createElement("div", { style: { fontSize: "11px", marginTop: "4px", color: "#999" } },
                                    "Last checked: ",
                                    new Date(providerHealth.lastChecked).toLocaleString()))),
                            !isEditing ? (React.createElement("div", null,
                                React.createElement("div", { style: { marginBottom: "12px", fontSize: "13px", lineHeight: "1.6", color: "#555", backgroundColor: "#f9fafb", padding: "8px", borderRadius: "4px" } },
                                    provider.baseUrl && React.createElement("div", null,
                                        React.createElement("strong", null, "URL:"),
                                        " ",
                                        React.createElement("code", { style: { fontSize: "11px", backgroundColor: "#fff", padding: "2px 4px", borderRadius: "2px", color: "#666" } }, provider.baseUrl)),
                                    React.createElement("div", null,
                                        React.createElement("strong", null, "Model:"),
                                        " ",
                                        provider.model),
                                    React.createElement("div", null,
                                        React.createElement("strong", null, "Capabilities:"),
                                        " ",
                                        React.createElement("span", { style: { fontSize: "12px" } }, ((_b = provider.capabilities) === null || _b === void 0 ? void 0 : _b.join(", ")) || "N/A")),
                                    provider.apiKeyEnvironmentVariable && React.createElement("div", null,
                                        React.createElement("strong", null, "Auth:"),
                                        " ",
                                        provider.apiKeyEnvironmentVariable)),
                                React.createElement("div", { style: { display: "flex", gap: "8px", flexWrap: "wrap" } },
                                    React.createElement("button", { className: "text-btn", onClick: () => this.testAiProvider(provider.id), disabled: isTesting, style: { flex: "1 1 auto", minWidth: "100px", padding: "8px", fontSize: "12px", border: "1px solid #ddd", borderRadius: "4px", cursor: "pointer", backgroundColor: isTesting ? "#f3f4f6" : "#fff", color: "#333" } }, isTesting ? React.createElement("span", null,
                                        React.createElement("span", { className: "spinner", style: { display: "inline-block", marginRight: "4px", verticalAlign: "middle" } }),
                                        "Testing\u2026") : "↻ Test"),
                                    React.createElement("button", { className: "text-btn", onClick: () => this.setState({ editingProvider: provider }), style: { flex: "1 1 auto", minWidth: "100px", padding: "8px", fontSize: "12px", border: "1px solid #ddd", borderRadius: "4px", cursor: "pointer", backgroundColor: "#fff", color: "#333" } }, "\u270E Configure")))) : (React.createElement("div", null,
                                React.createElement("div", { style: { marginBottom: "12px" } },
                                    React.createElement("label", { style: { display: "block", fontSize: "12px", fontWeight: 600, marginBottom: "4px" } }, "Provider ID"),
                                    React.createElement("input", { type: "text", value: editing.id, disabled: true, style: { width: "100%", padding: "6px", borderRadius: "4px", border: "1px solid #ddd", fontSize: "12px", backgroundColor: "#f3f4f6" } })),
                                React.createElement("div", { style: { marginBottom: "12px" } },
                                    React.createElement("label", { style: { display: "block", fontSize: "12px", fontWeight: 600, marginBottom: "4px" } }, "Display Name"),
                                    React.createElement("input", { type: "text", value: editing.displayName, onChange: (e) => this.setState({ editingProvider: { ...editing, displayName: e.target.value } }), style: { width: "100%", padding: "6px", borderRadius: "4px", border: "1px solid #ddd", fontSize: "12px" } })),
                                React.createElement("div", { style: { marginBottom: "12px" } },
                                    React.createElement("label", { style: { display: "block", fontSize: "12px", fontWeight: 600, marginBottom: "4px" } }, "Model"),
                                    React.createElement("input", { type: "text", value: editing.model, onChange: (e) => this.setState({ editingProvider: { ...editing, model: e.target.value } }), style: { width: "100%", padding: "6px", borderRadius: "4px", border: "1px solid #ddd", fontSize: "12px" } })),
                                React.createElement("div", { style: { marginBottom: "12px" } },
                                    React.createElement("label", { style: { display: "block", fontSize: "12px", fontWeight: 600, marginBottom: "4px" } }, "Base URL"),
                                    React.createElement("input", { type: "text", value: editing.baseUrl || "", onChange: (e) => this.setState({ editingProvider: { ...editing, baseUrl: e.target.value } }), style: { width: "100%", padding: "6px", borderRadius: "4px", border: "1px solid #ddd", fontSize: "12px" } })),
                                React.createElement("div", { style: { marginBottom: "12px" } },
                                    React.createElement("label", { style: { display: "block", fontSize: "12px", fontWeight: 600, marginBottom: "4px" } },
                                        React.createElement("input", { type: "checkbox", checked: editing.enabled, onChange: (e) => this.setState({ editingProvider: { ...editing, enabled: e.target.checked } }), style: { marginRight: "6px" } }),
                                        "Enabled")),
                                React.createElement("div", { style: { display: "flex", gap: "8px" } },
                                    React.createElement("button", { className: "success", onClick: () => this.saveAiProvider(), disabled: this.state.savingProvider, style: { flex: 1, padding: "8px", fontSize: "12px", border: "none", borderRadius: "4px", cursor: "pointer", backgroundColor: "#059669", color: "#fff" } }, this.state.savingProvider ? "Saving…" : "✓ Save changes"),
                                    React.createElement("button", { className: "text-btn", onClick: () => this.setState({ editingProvider: null }), style: { flex: 1, padding: "8px", fontSize: "12px", border: "1px solid #ddd", borderRadius: "4px", cursor: "pointer", backgroundColor: "#fff" } }, "Cancel")))));
                    })))),
                (aiProvidersState === null || aiProvidersState === void 0 ? void 0 : aiProvidersState.routes) && aiProvidersState.routes.length > 0 && (React.createElement("div", { className: "panel", style: { marginTop: "20px" } },
                    React.createElement("div", { className: "panel-head" },
                        React.createElement("div", null,
                            React.createElement("div", { className: "panel-title" }, "Routing configuration"),
                            React.createElement("div", { className: "panel-sub" }, "Task-to-provider routing rules"))),
                    React.createElement("div", { style: { padding: "16px" } }, aiProvidersState.routes.map(route => (React.createElement("div", { key: route.id, style: { marginBottom: "16px", padding: "12px", backgroundColor: defaultRoute === route.id ? "#f0fdf4" : "#f9fafb", borderRadius: "6px", border: defaultRoute === route.id ? "1px solid #86efac" : "1px solid #e5e7eb" } },
                        React.createElement("div", { style: { fontWeight: 600, marginBottom: "12px", color: defaultRoute === route.id ? "#059669" : "#333", display: "flex", alignItems: "center", gap: "8px" } },
                            route.id,
                            defaultRoute === route.id && React.createElement("span", { style: { fontSize: "11px", padding: "2px 8px", backgroundColor: "#d1fae5", borderRadius: "4px", color: "#065f46", fontWeight: 600 } }, "DEFAULT ROUTE")),
                        Object.entries(route.tasks).map(([task, providerList]) => (React.createElement("div", { key: task, style: { marginBottom: "8px", fontSize: "13px", color: "#555" } },
                            React.createElement("span", { style: { display: "inline-block", minWidth: "80px", fontWeight: 500, color: "#333" } },
                                task,
                                ":"),
                            React.createElement("span", { style: { fontFamily: "monospace", fontSize: "12px", backgroundColor: "#fff", padding: "2px 6px", borderRadius: "3px", color: "#666" } }, providerList.map((p, i) => (React.createElement("span", { key: p },
                                i > 0 && React.createElement("span", { style: { margin: "0 4px" } }, "\u2192"),
                                React.createElement("span", { style: { color: i === 0 ? "#059669" : "#666", fontWeight: i === 0 ? 600 : 400 } }, p)))))))))))),
                    React.createElement("div", { style: { padding: "16px", backgroundColor: "#fffbeb", borderRadius: "6px", border: "1px solid #fcd34d", fontSize: "13px" } },
                        React.createElement("span", { style: { fontWeight: 600 } }, "\uD83D\uDCA1 Provider fallback order:"),
                        " Tasks use the first available provider in order. If the primary provider fails or is disabled, the next provider in the chain handles the task.")))));
    }
    render() {
        let content;
        switch (this.state.view) {
            case "drafts":
                content = this.renderDraft();
                break;
            case "sources":
                content = this.renderSources();
                break;
            case "ai-models":
                content = this.renderAiModels();
                break;
            case "signals":
                content = this.renderPlaceholder("Content signals", "Search, filter, score, promote, and archive every collected engineering signal.");
                break;
            case "topics":
                content = this.renderPlaceholder("Topic map", "Tune the weighted taxonomy across .NET, AI, system design, cloud, DevOps, leadership, performance, security, and career growth.");
                break;
            case "settings":
                content = this.renderPlaceholder("Workspace settings", "Configure your profile voice, scheduler, JSON storage mode, provider routes, and review rules.");
                break;
            default: content = this.renderOverview();
        }
        return React.createElement("div", { className: "shell" },
            this.renderSidebar(),
            React.createElement("main", { className: "main" }, content),
            this.state.toast ? React.createElement("div", { className: "toast" },
                React.createElement("span", null, "\u2713"),
                this.state.toast) : null);
    }
}
ReactDOM.render(React.createElement(App, null), document.getElementById("root"));
