namespace DevSignalStudio.Application.Abstractions;

public interface IIngestionRunQueue
{
    ValueTask EnqueueAsync(string runId, CancellationToken cancellationToken);
    ValueTask<string> DequeueAsync(CancellationToken cancellationToken);
}

public interface IDraftGenerationQueue
{
    ValueTask EnqueueAsync(string runId, CancellationToken cancellationToken);
    ValueTask<string> DequeueAsync(CancellationToken cancellationToken);
}

public interface IRunCancellationRegistry
{
    CancellationTokenSource Register(string runId, CancellationToken hostCancellationToken);
    bool Cancel(string runId);
    void Complete(string runId);
}
