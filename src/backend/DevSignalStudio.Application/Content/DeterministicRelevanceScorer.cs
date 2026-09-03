using DevSignalStudio.Application.Abstractions;
using DevSignalStudio.Domain.Configuration;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Sources;

namespace DevSignalStudio.Application.Content;

public sealed class DeterministicRelevanceScorer : IRelevanceScorer
{
    private static readonly string[] LearningMarkers =
    {
        "how to", "guide", "tutorial", "architecture", "design", "trade-off", "performance",
        "security", "migration", "benchmark", "deep dive", "best practice", "case study"
    };

    private static readonly string[] DiscussionMarkers =
    {
        "why", "when", "should", "versus", "vs", "trade-off", "lessons", "mistakes", "future"
    };

    private static readonly string[] HypeMarkers =
    {
        "revolutionary", "game changer", "guaranteed", "must buy", "secret trick", "10x overnight",
        "you won't believe", "ultimate hack"
    };

    private readonly IClock _clock;

    public DeterministicRelevanceScorer(IClock clock)
    {
        _clock = clock;
    }

    public (ContentScore Score, IReadOnlyList<TopicMatch> Matches) Score(
        ContentItem item,
        SourceDefinition source,
        TopicTaxonomyDocument taxonomy,
        IReadOnlyList<ContentItem> existingItems)
    {
        string searchable = string.Join(
            " ",
            item.Title,
            item.Summary,
            item.Content,
            item.Notes,
            string.Join(" ", item.Tags));

        List<TopicMatch> matches = new();
        double weightedKeywordTotal = 0;

        foreach (TopicPillar pillar in taxonomy.Pillars)
        {
            List<string> terms = pillar.Keywords
                .Where(keyword => Contains(searchable, keyword.Term))
                .Select(keyword => keyword.Term)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (terms.Count == 0)
            {
                continue;
            }

            double pillarRaw = pillar.Keywords
                .Where(keyword => terms.Contains(keyword.Term, StringComparer.OrdinalIgnoreCase))
                .Sum(keyword => keyword.Weight) * pillar.Weight;

            weightedKeywordTotal += pillarRaw;
            matches.Add(new TopicMatch
            {
                PillarId = pillar.Id,
                PillarName = pillar.Name,
                Score = Round(ToSaturationScore(pillarRaw, 5.5)),
                MatchedTerms = terms
            });
        }

        matches = matches.OrderByDescending(match => match.Score).ToList();
        double topicRelevance = ToSaturationScore(weightedKeywordTotal, 8);
        double freshness = CalculateFreshness(item.PublishedAt ?? item.CollectedAt);
        double authority = Math.Clamp(source.TrustWeight * 100, 0, 100);
        double learningValue = CalculateLearningValue(item, searchable);
        double careerAlignment = CalculateCareerAlignment(matches, taxonomy);
        double novelty = CalculateNovelty(item, existingItems);
        double discussion = CalculateDiscussionPotential(item.Title, searchable);
        double hypePenalty = CalculateHypePenalty(searchable);

        double final =
            0.30 * topicRelevance +
            0.15 * freshness +
            0.15 * authority +
            0.15 * learningValue +
            0.10 * careerAlignment +
            0.10 * novelty +
            0.05 * discussion -
            hypePenalty;

        final = Math.Clamp(final, 0, 100);

        List<string> reasons = new();
        if (matches.Count > 0)
        {
            reasons.Add($"Matched {string.Join(", ", matches.Take(3).Select(match => match.PillarName))}.");
        }
        else
        {
            reasons.Add("No configured topic keywords matched.");
        }

        reasons.Add($"Source authority contributes {Round(authority):0.#}/100.");
        reasons.Add($"Freshness contributes {Round(freshness):0.#}/100.");
        if (hypePenalty > 0)
        {
            reasons.Add($"Hype language reduced the score by {Round(hypePenalty):0.#} points.");
        }

        ContentScore score = new()
        {
            FinalScore = Round(final),
            TopicRelevance = Round(topicRelevance),
            Freshness = Round(freshness),
            SourceAuthority = Round(authority),
            LearningValue = Round(learningValue),
            CareerAlignment = Round(careerAlignment),
            Novelty = Round(novelty),
            DiscussionPotential = Round(discussion),
            DuplicatePenalty = 0,
            HypePenalty = Round(hypePenalty),
            Reasons = reasons
        };

        return (score, matches);
    }

    private double CalculateFreshness(DateTimeOffset date)
    {
        double days = Math.Max(0, (_clock.UtcNow - date).TotalDays);
        return days switch
        {
            <= 1 => 100,
            <= 3 => 95,
            <= 7 => 88,
            <= 14 => 78,
            <= 30 => 65,
            <= 90 => 45,
            <= 365 => 25,
            _ => 10
        };
    }

    private static double CalculateLearningValue(ContentItem item, string searchable)
    {
        double score = 35;
        int length = (item.Summary?.Length ?? 0) + (item.Content?.Length ?? 0);
        score += Math.Min(30, length / 80d);
        score += LearningMarkers.Count(marker => Contains(searchable, marker)) * 6;
        if (!string.IsNullOrWhiteSpace(item.Notes))
        {
            score += 8;
        }

        return Math.Clamp(score, 0, 100);
    }

    private static double CalculateCareerAlignment(
        IReadOnlyList<TopicMatch> matches,
        TopicTaxonomyDocument taxonomy)
    {
        if (matches.Count == 0)
        {
            return 10;
        }

        Dictionary<string, TopicPillar> byId = taxonomy.Pillars.ToDictionary(pillar => pillar.Id);
        double alignment = matches.Sum(match =>
        {
            TopicPillar pillar = byId[match.PillarId];
            double priorityFactor = pillar.Priority switch
            {
                1 => 1.0,
                2 => 0.75,
                _ => 0.5
            };
            return match.Score * priorityFactor;
        });

        return Math.Clamp(alignment / Math.Max(1, Math.Min(matches.Count, 3)), 0, 100);
    }

    private static double CalculateNovelty(ContentItem item, IReadOnlyList<ContentItem> existingItems)
    {
        IReadOnlySet<string> current = ContentIdentity.Tokenize(item.Title);
        double maxSimilarity = existingItems
            .Where(existing => existing.Id != item.Id)
            .TakeLast(500)
            .Select(existing => ContentIdentity.Jaccard(current, ContentIdentity.Tokenize(existing.Title)))
            .DefaultIfEmpty(0)
            .Max();

        return Math.Clamp((1 - maxSimilarity) * 100, 10, 100);
    }

    private static double CalculateDiscussionPotential(string title, string searchable)
    {
        double score = 35;
        if (title.Contains('?'))
        {
            score += 20;
        }

        score += DiscussionMarkers.Count(marker => Contains(searchable, marker)) * 7;
        return Math.Clamp(score, 0, 100);
    }

    private static double CalculateHypePenalty(string searchable)
    {
        int hits = HypeMarkers.Count(marker => Contains(searchable, marker));
        return Math.Min(20, hits * 5);
    }

    private static double ToSaturationScore(double value, double scale) =>
        100 * (1 - Math.Exp(-Math.Max(0, value) / scale));

    private static bool Contains(string text, string term) =>
        !string.IsNullOrWhiteSpace(term) && text.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
