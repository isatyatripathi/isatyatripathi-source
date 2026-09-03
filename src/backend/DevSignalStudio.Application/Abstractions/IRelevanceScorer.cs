using DevSignalStudio.Domain.Configuration;
using DevSignalStudio.Domain.Content;
using DevSignalStudio.Domain.Sources;

namespace DevSignalStudio.Application.Abstractions;

public interface IRelevanceScorer
{
    (ContentScore Score, IReadOnlyList<TopicMatch> Matches) Score(
        ContentItem item,
        SourceDefinition source,
        TopicTaxonomyDocument taxonomy,
        IReadOnlyList<ContentItem> existingItems);
}
