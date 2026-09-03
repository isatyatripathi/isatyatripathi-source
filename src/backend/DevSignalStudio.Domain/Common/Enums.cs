namespace DevSignalStudio.Domain.Common;

public enum ContentItemStatus
{
    Collected,
    Candidate,
    Promoted,
    Drafted,
    Archived
}

public enum DraftStatus
{
    Generating,
    InReview,
    Approved,
    Rejected,
    Published,
    Archived
}

public enum RunStatus
{
    Queued,
    Running,
    Completed,
    CompletedWithWarnings,
    Failed,
    Cancelled
}

public enum HealthState
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy,
    Disabled
}

public enum ValidationSeverity
{
    Information,
    Warning,
    Error
}
