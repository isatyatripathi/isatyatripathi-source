namespace DevSignalStudio.Domain.Common;

public sealed record ValidationIssue
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;
    public string? Field { get; init; }
}

public sealed record ValidationReport
{
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = Array.Empty<ValidationIssue>();
    public DateTimeOffset ValidatedAt { get; init; }
    public bool IsValid => Issues.All(issue => issue.Severity != ValidationSeverity.Error);

    public static ValidationReport Success(DateTimeOffset now) => new() { ValidatedAt = now };
}
