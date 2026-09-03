using DevSignalStudio.Domain.Content;

namespace DevSignalStudio.Infrastructure.Sources;

internal static class CuratedItemMapper
{
    public static IReadOnlyList<RawContentItem> Map(CuratedItemsDocument document) =>
        document.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .Select(item => new RawContentItem
            {
                ExternalId = string.IsNullOrWhiteSpace(item.Id) ? item.Url ?? item.Title : item.Id,
                Title = item.Title,
                Url = item.Url,
                Summary = item.Summary,
                Content = item.Content,
                Author = item.Author,
                PublishedAt = item.PublishedAt,
                Tags = item.Tags,
                Notes = item.Notes
            })
            .ToArray();
}
