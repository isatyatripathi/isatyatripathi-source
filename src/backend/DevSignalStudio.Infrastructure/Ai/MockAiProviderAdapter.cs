using System.Diagnostics;
using System.Text.Json;
using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Ai;
using DevSignalStudio.Domain.Common;
using DevSignalStudio.Domain.Configuration;
using DevSignalStudio.Domain.Drafting;
using DevSignalStudio.Infrastructure.Common;

namespace DevSignalStudio.Infrastructure.Ai;

public sealed class MockAiProviderAdapter : IAiProviderAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Create(indented: false);
    private readonly IClock _clock;

    public MockAiProviderAdapter(IClock clock)
    {
        _clock = clock;
    }

    public string ProviderType => "mock";

    public async Task<AiResponse> GenerateAsync(
        AiProviderDefinition provider,
        AiRequest request,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        int delay = Math.Clamp(provider.GetInt("latencyMs", 20), 0, 2_000);
        if (delay > 0)
        {
            await Task.Delay(delay, cancellationToken);
        }

        DraftGenerationContext context = request.Context.Deserialize<DraftGenerationContext>(JsonOptions)
            ?? throw new InvalidDataException("The mock provider did not receive draft context.");
        var primary = context.Items.FirstOrDefault()
            ?? throw new InvalidDataException("At least one content item is required.");
        string topic = primary.TopicMatches.FirstOrDefault()?.PillarName ?? "software engineering";
        string evidence = primary.Summary ?? primary.Content ?? primary.Title;
        evidence = evidence.Length <= 260 ? evidence : evidence[..260] + "...";

        string body = BuildBody(context, primary.Title, topic, evidence);
        string[] hashtags = BuildHashtags(context);
        string diagram = BuildDiagram(primary.Title, topic);
        DraftAiOutput output = new()
        {
            Title = $"A practical way to learn from: {primary.Title}",
            Hook = $"A tech headline becomes useful only when it changes an engineering decision. Here is one way to turn this {topic} signal into practice.",
            Body = body,
            Hashtags = hashtags,
            Mermaid = diagram,
            Claims = new[]
            {
                new DraftAiClaim
                {
                    Text = $"The source focuses on '{primary.Title}'.",
                    SourceIds = new[] { primary.Id },
                    NeedsReview = false
                }
            }
        };

        return new AiResponse
        {
            Content = JsonSerializer.Serialize(output, JsonOptions),
            ProviderId = provider.Id,
            ProviderType = provider.Type,
            Model = provider.Model,
            DurationMilliseconds = stopwatch.ElapsedMilliseconds
        };
    }

    public Task<AiProviderHealth> CheckHealthAsync(
        AiProviderDefinition provider,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealth
        {
            ProviderId = provider.Id,
            Status = provider.Enabled ? HealthState.Healthy : HealthState.Disabled,
            Message = provider.Enabled
                ? "The deterministic mock provider is ready."
                : "The provider is disabled.",
            CheckedAt = _clock.UtcNow
        });

    private static string BuildBody(
        DraftGenerationContext context,
        string title,
        string topic,
        string evidence)
    {
        bool systemDesign = context.Recipe.Id.Contains("system-design", StringComparison.OrdinalIgnoreCase);
        bool leadership = context.Recipe.Id.Contains("leadership", StringComparison.OrdinalIgnoreCase);
        bool interview = context.Recipe.Id.Contains("interview", StringComparison.OrdinalIgnoreCase);

        if (systemDesign)
        {
            return $"""
Scenario
Use “{title}” as the starting signal, not as a complete architecture answer.

Requirements first
Define the user outcome, scale assumptions, latency target, failure tolerance, data sensitivity, and operating constraints before choosing components.

Architecture lens
Place the change inside a small boundary: input validation → application service → provider or infrastructure adapter → observable result. Keep vendor-specific code behind an interface so the core workflow remains testable.

Trade-off
Abstraction improves replaceability, but too many layers can hide important provider differences. Expose capabilities and limits instead of pretending every implementation is identical.

Evidence to verify
The source summary says: {evidence}

Interview lens
Explain what you would measure, how the design degrades when a dependency fails, and which decision you would postpone until real usage data exists.

Which assumption would you validate first?
""";
        }

        if (leadership)
        {
            return $"""
The tension
Teams can react to every new {topic} signal, or they can ignore change until it becomes urgent. Both extremes create avoidable cost.

The lesson
Turn “Should we adopt this?” into “What small, reversible experiment would reduce uncertainty?”

A practical practice
1. Capture the claim from the source.
2. Name the decision it might affect.
3. Define one success and one failure signal.
4. Time-box an experiment.
5. Record the result and the trade-off.

Source context
{evidence}

This keeps learning connected to delivery without turning the roadmap into a trend list.

How does your team decide which technology signals deserve an experiment?
""";
        }

        if (interview)
        {
            return $"""
Question
How would you evaluate and integrate the idea behind “{title}” into an existing full-stack .NET system?

Clarifying questions
What problem are we solving? What is the expected traffic? Which data is sensitive? What is the failure budget? Must the design work offline?

Approach
Start with contracts and boundaries. Keep deterministic validation before AI or remote calls, use cancellation and timeouts, preserve source provenance, and make the risky dependency replaceable.

Complexity
Discuss both computational complexity and operational complexity: latency, retries, rate limits, cost, observability, and recovery.

Pitfall
Do not jump directly to a product name. A strong answer connects requirements to a design and then explains why a technology fits.

Source context
{evidence}

What trade-off would you expect a senior candidate to surface first?
""";
        }

        return $"""
Why it matters
The useful question is not “Is {topic} popular?” It is “Which decision could this signal improve in a real .NET product?”

A practical learning loop
1. Verify the source and separate facts from opinion.
2. Map the idea to one architecture boundary or developer workflow.
3. Build the smallest experiment that can fail safely.
4. Measure quality, latency, cost, operability, and developer effort.
5. Share the result, including the limitation.

Source context
{evidence}

The trade-off
Moving quickly helps learning, but copying a pattern without requirements creates accidental complexity. Prefer a reversible experiment and an explicit exit criterion.

One action
Create a 60–90 minute spike that produces evidence: a test, trace, benchmark, ADR, or working vertical slice.

What evidence would make you adopt—or reject—this idea?
""";
    }

    private static string[] BuildHashtags(DraftGenerationContext context)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["dotnet-csharp"] = "#DotNet",
            ["frontend-react"] = "#React",
            ["ai-engineering"] = "#AIEngineering",
            ["mcp-prompting"] = "#MCP",
            ["system-design"] = "#SystemDesign",
            ["azure"] = "#Azure",
            ["aws-serverless"] = "#AWS",
            ["devops-platform"] = "#DevOps",
            ["performance-reliability"] = "#Performance",
            ["leadership-career"] = "#EngineeringLeadership",
            ["interviews-dsa"] = "#TechInterviews"
        };

        int maximum = context.Recipe.HashtagRange?.Max ?? 5;
        List<string> tags = context.Items
            .SelectMany(item => item.TopicMatches)
            .Select(match => map.GetValueOrDefault(match.PillarId))
            .Where(tag => tag is not null)
            .Select(tag => tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maximum - 1))
            .ToList();
        tags.Add("#SoftwareEngineering");
        return tags.Distinct(StringComparer.OrdinalIgnoreCase).Take(maximum).ToArray();
    }

    private static string BuildDiagram(string title, string topic)
    {
        string safeTitle = new(title.Where(character => char.IsLetterOrDigit(character) || character == ' ').ToArray());
        safeTitle = safeTitle.Length <= 42 ? safeTitle : safeTitle[..42] + "...";
        string safeTopic = new(topic.Where(character => char.IsLetterOrDigit(character) || character == ' ').ToArray());
        return $"""
flowchart LR
    A[Source signal: {safeTitle}] --> B[Verify facts]
    B --> C[Map to {safeTopic} decision]
    C --> D[Build reversible experiment]
    D --> E[Measure quality and trade-offs]
    E --> F[Share evidence]
""";
    }
}
