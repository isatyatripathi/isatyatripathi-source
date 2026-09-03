using DevSignalStudio.Application.Abstractions;

namespace DevSignalStudio.Infrastructure.Common;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class GuidIdGenerator : IIdGenerator
{
    public string NewId(string prefix)
    {
        string safePrefix = string.IsNullOrWhiteSpace(prefix) ? "id" : prefix.Trim().ToLowerInvariant();
        return $"{safePrefix}_{Guid.NewGuid():N}";
    }
}
