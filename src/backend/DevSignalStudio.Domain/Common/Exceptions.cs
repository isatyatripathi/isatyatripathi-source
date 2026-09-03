namespace DevSignalStudio.Domain.Common;

public abstract class DevSignalException : Exception
{
    protected DevSignalException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    protected DevSignalException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class DomainRuleException : DevSignalException
{
    public DomainRuleException(string code, string message)
        : base(code, message)
    {
    }
}

public sealed class ResourceNotFoundException : DevSignalException
{
    public ResourceNotFoundException(string resourceType, string id)
        : base("resource_not_found", $"{resourceType} '{id}' was not found.")
    {
    }
}

public sealed class ConcurrencyConflictException : DevSignalException
{
    public ConcurrencyConflictException(string resourceType, string id, int expected, int actual)
        : base(
            "concurrency_conflict",
            $"{resourceType} '{id}' has revision {actual}; revision {expected} was expected.")
    {
    }
}

public sealed class RequestValidationException : DevSignalException
{
    public RequestValidationException(string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base("validation_failed", message)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
