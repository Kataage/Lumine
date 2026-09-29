using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls.ApplicationLifetimes;
using Lumine.Diagnostics;
using Lumine.Image;

namespace Lumine.App;

internal sealed record RealLibraryAcceptanceOptions(
    string LibraryRoot,
    AppDataPaths DataPaths,
    string OutputPath,
    string Mode,
    int MinimumAssets,
    TimeSpan BrowseDuration,
    TimeSpan IdleDuration)
{
    public static RealLibraryAcceptanceOptions Parse(
        IReadOnlyList<string> args)
    {
        var libraryRoot =
            GetRequiredValue(
                args,
                "--library-dir=");
        var dataRoot =
            GetRequiredValue(
                args,
                "--data-dir=");
        var output =
            GetRequiredValue(
                args,
                "--output=");

        var mode =
            GetOptionalValue(
                args,
                "--mode=")
            ?? "unspecified";

        var minimumAssets =
            ParseInt(
                GetOptionalValue(
                    args,
                    "--min-assets="),
                defaultValue: 1000,
                minimum: 1,
                maximum: 10_000_000,
                "min-assets");

        var browseSeconds =
            ParseInt(
                GetOptionalValue(
                    args,
                    "--browse-seconds="),
                defaultValue: 60,
                minimum: 1,
                maximum: 3600,
                "browse-seconds");

        var idleSeconds =
            ParseInt(
                GetOptionalValue(
                    args,
                    "--idle-seconds="),
                defaultValue: 10,
                minimum: 1,
                maximum: 600,
                "idle-seconds");

        return new RealLibraryAcceptanceOptions(
            Path.GetFullPath(libraryRoot),
            AppDataPaths.FromRoot(dataRoot),
            Path.GetFullPath(output),
            mode,
            minimumAssets,
            TimeSpan.FromSeconds(browseSeconds),
            TimeSpan.FromSeconds(idleSeconds));
    }

    private static string GetRequiredValue(
        IReadOnlyList<string> args,
        string prefix)
    {
        var value =
            GetOptionalValue(args, prefix);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"Core acceptance requires {prefix}<value>.");
        }

        return value;
    }

    private static string? GetOptionalValue(
        IReadOnlyList<string> args,
        string prefix)
    {
        var argument =
            args.FirstOrDefault(
                arg => arg.StartsWith(
                    prefix,
                    StringComparison.Ordinal));

        return argument is null
            ? null
            : argument[prefix.Length..];
    }

    private static int ParseInt(
        string? value,
        int defaultValue,
        int minimum,
        int maximum,
        string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw new ArgumentOutOfRangeException(
                name,
                $"Acceptance {name} must be between {minimum} and {maximum}.");
        }

        return parsed;
    }
}

internal sealed class RealLibraryAcceptanceSession
{
    public const string Switch =
        "--core-acceptance";

    private readonly BenchmarkRecorder _recorder =
        new();
    private readonly MeasurementStart _startup =
        BenchmarkRecorder.CaptureStart();
    private readonly PeakWorkingSetMonitor _peak =
        PeakWorkingSetMonitor.Start(
            TimeSpan.FromMilliseconds(25));
    private readonly Dictionary<string, string> _metadata =
        new(StringComparer.Ordinal);
    private int _windowReadyRecorded;
    private int _runStarted;

    private RealLibraryAcceptanceSession(
        RealLibraryAcceptanceOptions options)
    {
        Options = options;
    }

    public RealLibraryAcceptanceOptions Options { get; }

    public AppDataPaths DataPaths =>
        Options.DataPaths;

    public static bool IsRequested(
        IReadOnlyList<string> args) =>
        args.Any(
            static arg => string.Equals(
                arg,
                Switch,
                StringComparison.Ordinal));

    public static RealLibraryAcceptanceSession? TryCreate(
        IReadOnlyList<string> args) =>
        IsRequested(args)
            ? new RealLibraryAcceptanceSession(
                RealLibraryAcceptanceOptions.Parse(args))
            : null;

    public void MarkWindowReady()
    {
        if (Interlocked.Exchange(
                ref _windowReadyRecorded,
                1) != 0)
        {
            return;
        }

        _recorder.Complete(
            CoreMetricNames.StartupWindowReady,
            _startup);
    }

    public void Start(
        MainWindow window,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(desktop);

        if (Interlocked.Exchange(
                ref _runStarted,
                1) != 0)
        {
            return;
        }

        _ = RunAndShutdownObservedAsync(
            window,
            desktop);
    }

    private async Task RunAndShutdownObservedAsync(
        MainWindow window,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            await RunAndShutdownAsync(
                window,
                desktop);
        }
        catch (Exception exception)
        {
            Program.Host?.Log.Write(
                "acceptance",
                $"Unhandled acceptance failure: {exception}");

            Console.Error.WriteLine(
                $"Unhandled real-library acceptance failure: {exception}");

            try
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
            catch
            {
            }

            desktop.Shutdown(2);
        }
    }

    private async Task RunAndShutdownAsync(
        MainWindow window,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var exitCode = 0;
        Exception? failure = null;

        _metadata["acceptance.schema"] = "1";
        _metadata["acceptance.mode"] =
            Options.Mode;
        _metadata["acceptance.minimum_assets"] =
            Options.MinimumAssets.ToString(
                CultureInfo.InvariantCulture);
        _metadata["acceptance.browse_seconds"] =
            Options.BrowseDuration.TotalSeconds.ToString(
                CultureInfo.InvariantCulture);
        _metadata["acceptance.idle_seconds"] =
            Options.IdleDuration.TotalSeconds.ToString(
                CultureInfo.InvariantCulture);
        _metadata["library.path_sha256"] =
            HashPath(Options.LibraryRoot);
        _metadata["gpu.memory"] =
            "not-collected-core-has-no-owned-ai-gpu-runtime";

        foreach (var pair in
                 Program.ResourcePolicy
                     .ToDiagnosticMetadata())
        {
            _metadata[pair.Key] =
                pair.Value;
        }

        try
        {
            await RunCoreAsync(window);
        }
        catch (Exception exception)
        {
            exitCode = 2;
            failure = exception;
            RecordFailure(exception);
        }

        var shutdownStart =
            BenchmarkRecorder.CaptureStart();

        try
        {
            window.Close();

            await WaitUntilAsync(
                () => !window.IsVisible,
                TimeSpan.FromSeconds(30),
                "MainWindow did not complete coordinated acceptance shutdown.");

            _recorder.Complete(
                "acceptance.clean_shutdown",
                shutdownStart);
            _metadata["shutdown.window_closed"] =
                "true";

            var postShutdownCache =
                new ThumbnailCache(
                    Options.DataPaths.ThumbnailCachePath,
                    Program.ResourcePolicy);
            var postShutdownStats =
                await postShutdownCache.GetStatsAsync();

            WriteCacheMetadata(
                "thumbnail.cache_post_shutdown",
                postShutdownStats);
            _metadata[
                "thumbnail.cache_post_shutdown.configured_bytes"] =
                postShutdownCache.ConfiguredByteLimit.ToString(
                    CultureInfo.InvariantCulture);

            if (postShutdownStats.TotalBytes
                > postShutdownCache.ConfiguredByteLimit)
            {
                throw new InvalidOperationException(
                    $"Post-shutdown thumbnail cache exceeds its configured disk budget: {postShutdownStats.TotalBytes:N0} > {postShutdownCache.ConfiguredByteLimit:N0} bytes.");
            }

            if (postShutdownStats.InterruptedWriteCount != 0)
            {
                throw new InvalidOperationException(
                    $"Post-shutdown thumbnail cache still contains {postShutdownStats.InterruptedWriteCount:N0} interrupted write(s).");
            }
        }
        catch (Exception exception)
        {
            exitCode = 2;
            failure ??= exception;
            _metadata["shutdown.window_closed"] =
                "false";
            _metadata["shutdown.failure"] =
                NormalizeMetadataValue(
                    exception.Message);
        }

        await _peak.DisposeAsync();

        _metadata["resource.peak_working_set_bytes"] =
            _peak.PeakWorkingSetBytes.ToString(
                CultureInfo.InvariantCulture);
        _metadata["resource.peak_additional_working_set_bytes"] =
            _peak.PeakAdditionalWorkingSetBytes.ToString(
                CultureInfo.InvariantCulture);

        if (failure is null)
        {
            try
            {
                await CoreAcceptanceFunctionalScenario.RunAsync(
                    _recorder,
                    _metadata);
            }
            catch (Exception exception)
            {
                exitCode = 2;
                failure = exception;
                RecordFailure(exception);
            }
        }

        _metadata["acceptance.automated_result"] =
            failure is null
                ? "pass"
                : "fail";

        if (failure is not null)
        {
            Program.Host?.Log.Write(
                "acceptance",
                $"Real-library acceptance failed: {failure.Message}");
        }
        else
        {
            Program.Host?.Log.Write(
                "acceptance",
                "Real-library automated acceptance completed.");
        }

        try
        {
            await _recorder.WriteJsonAsync(
                Options.OutputPath,
                _metadata);
        }
        catch (Exception exception)
        {
            exitCode = 2;
            Console.Error.WriteLine(
                $"Unable to write acceptance output: {exception}");
        }

        desktop.Shutdown(exitCode);
    }

    private async Task RunCoreAsync(
        MainWindow window)
    {
        if (!Directory.Exists(
                Options.LibraryRoot))
        {
            throw new DirectoryNotFoundException(
                $"Acceptance library does not exist: {Options.LibraryRoot}");
        }

        using (_recorder.Measure(
                   "acceptance.library_open"))
        {
            await window.OpenLibraryAsync(
                Options.LibraryRoot,
                Options.DataPaths);
        }

        var runtime =
            window.CurrentRuntime
            ?? throw new InvalidOperationException(
                "Acceptance library did not produce a CoreViewerRuntime.");
        var shell =
            window.CurrentShell
            ?? throw new InvalidOperationException(
                "Acceptance library did not produce a CoreViewerShell.");

        if (runtime.AssetCount
            < Options.MinimumAssets)
        {
            throw new InvalidOperationException(
                $"Acceptance library has {runtime.AssetCount:N0} assets; at least {Options.MinimumAssets:N0} are required.");
        }

        _metadata["library.asset_count"] =
            runtime.AssetCount.ToString(
                CultureInfo.InvariantCulture);
        _metadata["filesystem.bootstrap_mode"] =
            runtime.SyncSession.BootstrapMode.ToString();

        var cacheBefore =
            await runtime.ThumbnailCache.GetStatsAsync();
        WriteCacheMetadata(
            "thumbnail.cache_before",
            cacheBefore);

        var grid = shell.GridViewer;
        var detail = shell.DetailViewer;

        var viewportStart =
            BenchmarkRecorder.CaptureStart();

        await WaitForGridReadyAsync(
            grid,
            TimeSpan.FromSeconds(60));

        _recorder.Complete(
            CoreMetricNames.ViewerFirstViewportReady,
            viewportStart);

        var scrollTargets =
            CreateScrollTargets(
                runtime.AssetCount);

        foreach (var target in scrollTargets)
        {
            await MeasureScrollAsync(
                grid,
                target);
        }

        await MeasureDetailAsync(
            detail,
            runtime.AssetCount);

        using (_recorder.Measure(
                   "acceptance.long_browse"))
        {
            await ExerciseLongBrowseAsync(
                grid,
                detail,
                scrollTargets,
                Options.BrowseDuration);
        }

        using (_recorder.Measure(
                   "acceptance.idle_settle"))
        {
            await Task.Delay(
                Options.IdleDuration);
        }

        var viewer =
            grid.Diagnostics;
        var image =
            runtime.ThumbnailPipelineDiagnostics;
        var maintenance =
            runtime.ThumbnailCacheMaintenanceDiagnostics;
        var sync =
            runtime.SyncSession.Diagnostics;
        var cacheAfter =
            await runtime.ThumbnailCache.GetStatsAsync();

        WriteViewerMetadata(viewer);
        WriteImageMetadata(image);
        WriteMaintenanceMetadata(maintenance);
        WriteSyncMetadata(sync);
        WriteCacheMetadata(
            "thumbnail.cache_after",
            cacheAfter);
        _metadata["thumbnail.cache_configured_bytes"] =
            runtime.ThumbnailCache.ConfiguredByteLimit.ToString(
                CultureInfo.InvariantCulture);

        if (cacheAfter.TotalBytes
            > runtime.ThumbnailCache.ConfiguredByteLimit)
        {
            throw new InvalidOperationException(
                $"Persistent thumbnail cache exceeds its configured disk budget: {cacheAfter.TotalBytes:N0} > {runtime.ThumbnailCache.ConfiguredByteLimit:N0} bytes.");
        }

        if (viewer.TileLoadFailures != 0)
        {
            throw new InvalidOperationException(
                $"Viewer reported {viewer.TileLoadFailures} visible tile load failures: {viewer.LastTileLoadError}");
        }

        if (viewer.ThumbnailRequestsFailed != 0)
        {
            throw new InvalidOperationException(
                $"Viewer reported {viewer.ThumbnailRequestsFailed} thumbnail request failure(s) after the idle-settle window.");
        }

        if (viewer.InFlightThumbnailRequests != 0)
        {
            throw new InvalidOperationException(
                $"Viewer still had {viewer.InFlightThumbnailRequests} in-flight thumbnail request(s) after the idle-settle window.");
        }

        if (viewer.ActiveBitmapDecodes != 0)
        {
            throw new InvalidOperationException(
                $"Viewer still had {viewer.ActiveBitmapDecodes} active bitmap decode(s) after the idle-settle window.");
        }

        if (image.Failed != 0)
        {
            throw new InvalidOperationException(
                $"Thumbnail generation recorded {image.Failed} failure(s) during acceptance.");
        }

        if (maintenance.RunsFailed != 0)
        {
            throw new InvalidOperationException(
                $"Thumbnail cache maintenance recorded {maintenance.RunsFailed} failure(s): {maintenance.LastError}");
        }

        if (sync.ReconcileFailures != 0)
        {
            throw new InvalidOperationException(
                $"Filesystem synchronization recorded {sync.ReconcileFailures} reconciliation failure(s).");
        }

        if (sync.QueueDepth != 0)
        {
            throw new InvalidOperationException(
                $"Filesystem synchronization still had queue depth {sync.QueueDepth} after the idle-settle window.");
        }

        if (sync.EventsObserved != 0)
        {
            throw new InvalidOperationException(
                $"Representative library changed during acceptance; watcher observed {sync.EventsObserved} filesystem event(s).");
        }

        if (sync.Reconciliations != 0)
        {
            throw new InvalidOperationException(
                $"Runtime filesystem synchronization performed {sync.Reconciliations} reconciliation(s); periodic/overflow full reconciliation is not accepted for the stable representative library.");
        }

        if (viewer.DecodedBitmapEntries
            > runtime.ViewerSession.Options.DecodedBitmapEntryLimit)
        {
            throw new InvalidOperationException(
                "Decoded thumbnail cache exceeded its configured entry bound.");
        }

        if (viewer.DecodedBitmapBytes
            > runtime.ViewerSession.Options.DecodedBitmapByteLimit)
        {
            throw new InvalidOperationException(
                "Decoded thumbnail cache exceeded its configured byte bound.");
        }
    }

    private async Task MeasureScrollAsync(
        Lumine.Viewer.ThumbnailViewerControl grid,
        long target)
    {
        var start =
            BenchmarkRecorder.CaptureStart();
        var failuresBefore =
            grid.Diagnostics.TileLoadFailures;

        grid.ScrollToAsset(target);

        await WaitForScrollTargetAsync(
            grid,
            target,
            TimeSpan.FromSeconds(30));

        _recorder.Complete(
            CoreMetricNames.ViewerFastScrollRefresh,
            start);

        var failuresAfter =
            grid.Diagnostics.TileLoadFailures;

        if (failuresAfter != failuresBefore)
        {
            throw new InvalidOperationException(
                $"Visible tile failures increased while scrolling to {target:N0}.");
        }
    }

    private async Task MeasureDetailAsync(
        Lumine.Viewer.DetailViewerControl detail,
        long assetCount)
    {
        var index =
            Math.Clamp(
                assetCount / 2,
                0,
                assetCount - 1);

        using (_recorder.Measure(
                   "acceptance.detail_preview"))
        {
            await detail.SelectAsync(index);
        }

        RequireHealthyDetail(
            detail,
            "detail preview");

        if (index + 1 < assetCount)
        {
            using (_recorder.Measure(
                       "acceptance.detail_next"))
            {
                await detail.SelectAsync(
                    index + 1);
            }

            RequireHealthyDetail(
                detail,
                "detail next");
        }

        using (_recorder.Measure(
                   "acceptance.detail_previous"))
        {
            await detail.SelectAsync(index);
        }

        RequireHealthyDetail(
            detail,
            "detail previous");

        using (_recorder.Measure(
                   "acceptance.detail_zoom"))
        {
            await detail.ZoomByAsync(1.25);
        }

        detail.PanBy(24, 16);
        detail.Fit();

        using (_recorder.Measure(
                   "acceptance.detail_actual_size"))
        {
            await detail.ActualSizeAsync();
        }

        RequireHealthyDetail(
            detail,
            "detail actual-size");

        _metadata["detail.actual_size_original"] =
            detail.IsOriginal
                ? "true"
                : "false";

        detail.Fit();
    }

    private async Task ExerciseLongBrowseAsync(
        Lumine.Viewer.ThumbnailViewerControl grid,
        Lumine.Viewer.DetailViewerControl detail,
        IReadOnlyList<long> targets,
        TimeSpan duration)
    {
        var started =
            System.Diagnostics.Stopwatch.StartNew();
        var iteration = 0;

        while (started.Elapsed < duration)
        {
            var target =
                targets[
                    iteration
                    % targets.Count];

            grid.ScrollToAsset(target);

            await WaitForScrollTargetAsync(
                grid,
                target,
                TimeSpan.FromSeconds(30));

            if ((iteration % 4) == 0)
            {
                await detail.SelectAsync(target);
                RequireHealthyDetail(
                    detail,
                    "long browse detail");
            }

            iteration++;

            await Task.Delay(100);
        }

        _metadata["browsing.long_iterations"] =
            iteration.ToString(
                CultureInfo.InvariantCulture);
    }

    private static Task WaitForScrollTargetAsync(
        Lumine.Viewer.ThumbnailViewerControl grid,
        long target,
        TimeSpan timeout) =>
        WaitUntilAsync(
            () =>
            {
                var first =
                    grid.FirstVisibleAssetIndex;
                var last =
                    grid.LastVisibleAssetIndex;

                return first is not null
                    && last is not null
                    && target >= first.Value
                    && target <= last.Value
                    && grid.IsAssetReady(target);
            },
            timeout,
            $"Grid did not settle near asset {target:N0}.");

    private static async Task WaitForGridReadyAsync(
        Lumine.Viewer.ThumbnailViewerControl grid,
        TimeSpan timeout)
    {
        await WaitUntilAsync(
            () =>
            {
                var diagnostics =
                    grid.Diagnostics;

                var required =
                    Math.Max(
                        1,
                        Math.Min(
                            4,
                            diagnostics.AttachedTiles));

                return diagnostics.AttachedTiles > 0
                    && diagnostics.ReadyTiles >= required
                    && diagnostics.TileLoadFailures == 0;
            },
            timeout,
            "Initial grid viewport did not become ready.");
    }

    private static void RequireHealthyDetail(
        Lumine.Viewer.DetailViewerControl detail,
        string operation)
    {
        if (detail.LoadState
            == Lumine.Viewer.ViewerDetailLoadState.Error)
        {
            throw new InvalidOperationException(
                $"{operation} ended in the Detail error state.");
        }
    }

    private static IReadOnlyList<long> CreateScrollTargets(
        long count)
    {
        if (count <= 1)
        {
            return [0];
        }

        return
        [
            0,
            count / 4,
            count / 2,
            (count * 3) / 4,
            count - 1,
            count / 2,
            0
        ];
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string failureMessage)
    {
        var started =
            System.Diagnostics.Stopwatch.StartNew();

        while (!condition())
        {
            if (started.Elapsed >= timeout)
            {
                throw new TimeoutException(
                    failureMessage);
            }

            await Task.Delay(16);
        }
    }

    private void WriteViewerMetadata(
        Lumine.Viewer.ViewerRuntimeDiagnostics diagnostics)
    {
        _metadata["viewer.thumbnail_requests"] =
            diagnostics.ThumbnailRequests.ToString(
                CultureInfo.InvariantCulture);
        _metadata["viewer.thumbnail_requests_failed"] =
            diagnostics.ThumbnailRequestsFailed.ToString(
                CultureInfo.InvariantCulture);
        _metadata["viewer.thumbnail_requests_cancelled"] =
            diagnostics.ThumbnailRequestsCancelled.ToString(
                CultureInfo.InvariantCulture);
        _metadata["viewer.in_flight_thumbnail_requests"] =
            diagnostics.InFlightThumbnailRequests.ToString(
                CultureInfo.InvariantCulture);
        _metadata["viewer.active_bitmap_decodes"] =
            diagnostics.ActiveBitmapDecodes.ToString(
                CultureInfo.InvariantCulture);
        _metadata["viewer.tile_load_failures"] =
            diagnostics.TileLoadFailures.ToString(
                CultureInfo.InvariantCulture);
        _metadata["viewer.decoded_bitmap_entries"] =
            diagnostics.DecodedBitmapEntries.ToString(
                CultureInfo.InvariantCulture);
        _metadata["viewer.decoded_bitmap_bytes"] =
            diagnostics.DecodedBitmapBytes.ToString(
                CultureInfo.InvariantCulture);
        _metadata["viewer.peak_bitmap_decodes"] =
            diagnostics.PeakConcurrentBitmapDecodes.ToString(
                CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(
                diagnostics.LastTileLoadError))
        {
            _metadata["viewer.last_tile_load_error"] =
                NormalizeMetadataValue(
                    diagnostics.LastTileLoadError);
        }
    }

    private void WriteImageMetadata(
        ThumbnailDiagnosticsSnapshot diagnostics)
    {
        _metadata["thumbnail.cache_hits"] =
            diagnostics.CacheHits.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.cache_misses"] =
            diagnostics.CacheMisses.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.generated"] =
            diagnostics.Generated.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.failed"] =
            diagnostics.Failed.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.source_opens"] =
            diagnostics.SourceOpens.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.metadata_probes"] =
            diagnostics.MetadataProbes.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.metadata_bytes_hashed"] =
            diagnostics.MetadataBytesHashed.ToString(
                CultureInfo.InvariantCulture);
    }

    private void WriteMaintenanceMetadata(
        ThumbnailCacheMaintenanceDiagnosticsSnapshot diagnostics)
    {
        _metadata["thumbnail.maintenance_runs_scheduled"] =
            diagnostics.RunsScheduled.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.maintenance_runs_started"] =
            diagnostics.RunsStarted.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.maintenance_runs_completed"] =
            diagnostics.RunsCompleted.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.maintenance_runs_cancelled"] =
            diagnostics.RunsCancelled.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.maintenance_runs_failed"] =
            diagnostics.RunsFailed.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.maintenance_foreground_preemptions"] =
            diagnostics.ForegroundPreemptions.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.maintenance_files_deleted"] =
            diagnostics.FilesDeleted.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.maintenance_bytes_deleted"] =
            diagnostics.BytesDeleted.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.maintenance_interrupted_writes_deleted"] =
            diagnostics.InterruptedWritesDeleted.ToString(
                CultureInfo.InvariantCulture);
        _metadata["thumbnail.maintenance_last_bytes_after"] =
            diagnostics.LastBytesAfter.ToString(
                CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(
                diagnostics.LastError))
        {
            _metadata["thumbnail.maintenance_last_error"] =
                NormalizeMetadataValue(
                    diagnostics.LastError);
        }
    }

    private void WriteSyncMetadata(
        Lumine.Library.LibrarySyncDiagnostics diagnostics)
    {
        _metadata["filesystem.events_observed"] =
            diagnostics.EventsObserved.ToString(
                CultureInfo.InvariantCulture);
        _metadata["filesystem.events_applied"] =
            diagnostics.EventsApplied.ToString(
                CultureInfo.InvariantCulture);
        _metadata["filesystem.reconciliations"] =
            diagnostics.Reconciliations.ToString(
                CultureInfo.InvariantCulture);
        _metadata["filesystem.reconcile_failures"] =
            diagnostics.ReconcileFailures.ToString(
                CultureInfo.InvariantCulture);
        _metadata["filesystem.max_apply_latency_ms"] =
            diagnostics.MaxApplyLatencyMs.ToString(
                "F3",
                CultureInfo.InvariantCulture);
        _metadata["filesystem.queue_depth"] =
            diagnostics.QueueDepth.ToString(
                CultureInfo.InvariantCulture);
    }

    private void WriteCacheMetadata(
        string prefix,
        ThumbnailCacheStats stats)
    {
        _metadata[$"{prefix}.files"] =
            stats.FileCount.ToString(
                CultureInfo.InvariantCulture);
        _metadata[$"{prefix}.bytes"] =
            stats.TotalBytes.ToString(
                CultureInfo.InvariantCulture);
        _metadata[$"{prefix}.interrupted_writes"] =
            stats.InterruptedWriteCount.ToString(
                CultureInfo.InvariantCulture);
    }

    private static string HashPath(
        string path)
    {
        var normalized =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(path))
            .ToUpperInvariant();

        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        normalized)))
            .ToLowerInvariant();
    }

    private void RecordFailure(
        Exception exception)
    {
        _metadata["acceptance.failure_type"] =
            exception.GetType().Name;
        _metadata["acceptance.failure"] =
            NormalizeMetadataValue(
                exception.Message);
    }

    private static string NormalizeMetadataValue(
        string value) =>
        value
            .Replace(
                '\r',
                ' ')
            .Replace(
                '\n',
                ' ')
            .Trim();
}
