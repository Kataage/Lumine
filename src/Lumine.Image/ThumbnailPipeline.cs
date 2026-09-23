using System.Threading.Channels;

namespace Lumine.Image;

public sealed class ThumbnailPipeline : IAsyncDisposable
{
    private readonly ThumbnailGenerator _generator;
    private readonly Channel<WorkItem> _foreground;
    private readonly Channel<WorkItem> _background;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;

    public ThumbnailPipeline(
        ThumbnailCache cache,
        ThumbnailPipelineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cache);

        options ??= new ThumbnailPipelineOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.WorkerCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.QueueCapacity);

        WorkerCount = options.WorkerCount;
        QueueCapacity = options.QueueCapacity;
        _generator = new ThumbnailGenerator(cache);

        _foreground = CreateQueue(options.QueueCapacity);
        _background = CreateQueue(options.QueueCapacity);
        _workers = Enumerable.Range(0, options.WorkerCount)
            .Select(_ => Task.Run(WorkerLoopAsync))
            .ToArray();
    }

    public int WorkerCount { get; }

    public int QueueCapacity { get; }

    public ThumbnailDiagnosticsSnapshot Diagnostics =>
        _generator.SnapshotDiagnostics();

    public async Task<ThumbnailResult> RequestAsync(
        ThumbnailSource source,
        ThumbnailProfile profile,
        ThumbnailPriority priority = ThumbnailPriority.Foreground,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_shutdown.IsCancellationRequested, this);

        var completion = new TaskCompletionSource<ThumbnailResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new WorkItem(source, profile, completion, cancellationToken);

        var writer = priority == ThumbnailPriority.Foreground
            ? _foreground.Writer
            : _background.Writer;

        await writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        _foreground.Writer.TryComplete();
        _background.Writer.TryComplete();
        _shutdown.Cancel();

        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private static Channel<WorkItem> CreateQueue(int capacity) =>
        Channel.CreateBounded<WorkItem>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

    private async Task WorkerLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            WorkItem? item = null;

            if (_foreground.Reader.TryRead(out var foreground))
            {
                item = foreground;
            }
            else if (_background.Reader.TryRead(out var background))
            {
                item = background;
            }
            else
            {
                var foregroundWait = _foreground.Reader
                    .WaitToReadAsync(_shutdown.Token)
                    .AsTask();
                var backgroundWait = _background.Reader
                    .WaitToReadAsync(_shutdown.Token)
                    .AsTask();

                await Task.WhenAny(foregroundWait, backgroundWait).ConfigureAwait(false);
                continue;
            }

            if (item.CancellationToken.IsCancellationRequested)
            {
                item.Completion.TrySetCanceled(item.CancellationToken);
                continue;
            }

            try
            {
                var result = _generator.GetOrCreate(
                    item.Source,
                    item.Profile,
                    item.CancellationToken);

                item.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException)
                when (item.CancellationToken.IsCancellationRequested)
            {
                item.Completion.TrySetCanceled(item.CancellationToken);
            }
            catch (Exception exception)
            {
                item.Completion.TrySetException(exception);
            }
        }
    }

    private sealed record WorkItem(
        ThumbnailSource Source,
        ThumbnailProfile Profile,
        TaskCompletionSource<ThumbnailResult> Completion,
        CancellationToken CancellationToken);
}
