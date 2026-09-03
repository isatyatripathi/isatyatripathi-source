using System.Collections.Concurrent;
using System.Threading.Channels;
using DevSignalStudio.Application.Abstractions;

namespace DevSignalStudio.Infrastructure.Workers;

public sealed class IngestionRunQueue : IIngestionRunQueue
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public ValueTask EnqueueAsync(string runId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(runId, cancellationToken);

    public ValueTask<string> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}

public sealed class DraftGenerationQueue : IDraftGenerationQueue
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public ValueTask EnqueueAsync(string runId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(runId, cancellationToken);

    public ValueTask<string> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}

public sealed class RunCancellationRegistry : IRunCancellationRegistry, IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tokens =
        new(StringComparer.OrdinalIgnoreCase);

    public CancellationTokenSource Register(string runId, CancellationToken hostCancellationToken)
    {
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
        CancellationTokenSource existing = _tokens.AddOrUpdate(
            runId,
            source,
            (_, previous) =>
            {
                previous.Cancel();
                previous.Dispose();
                return source;
            });
        return existing;
    }

    public bool Cancel(string runId)
    {
        if (!_tokens.TryGetValue(runId, out CancellationTokenSource? source))
        {
            return false;
        }

        source.Cancel();
        return true;
    }

    public void Complete(string runId)
    {
        if (_tokens.TryRemove(runId, out CancellationTokenSource? source))
        {
            source.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (CancellationTokenSource source in _tokens.Values)
        {
            source.Cancel();
            source.Dispose();
        }
        _tokens.Clear();
    }
}
