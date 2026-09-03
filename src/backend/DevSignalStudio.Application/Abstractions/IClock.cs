namespace DevSignalStudio.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IIdGenerator
{
    string NewId(string prefix);
}
