using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using Avalonia.VisualTree;
using Lumine.Viewer;

namespace Lumine.Viewer.Benchmarks;

// Opt-in experimental first-tick raster evidence. Avalonia's documented
// Window.MouseWheel helper calls RunJobsAndRender before AND after input.
// Using that helper cannot inspect the first explicit rendering tick;
// the public PointerWheelEventArgs + InputElement.RaiseEvent API
// allows a routed wheel without the implicit render-flush helper. This
// deliberately never runs in the baseline timing/performance sample.
internal readonly record struct UnflushedRoutedWheelFrameEvidence(
    long RoutedEvents,
    double OffsetDeltaPixels,
    int DirectionBefore,
    int DirectionAfter,
    int UnreadyImmediatelyAfterInput,
    bool FirstTickContainsUpdatedRaster,
    bool PreInputWitnessValid,
    bool FirstTickWitnessChanged,
    int PostDispatchPasses,
    bool PostDispatchWitnessChanged,
    bool PostDispatchRasterChanged,
    RenderedFrameTileAudit FirstTick,
    RenderedFrameTileAudit PostDispatch,
    RenderedFrameTileAudit Settled);

internal static class UnflushedRoutedWheelFirstFrameProbe
{
    internal static UnflushedRoutedWheelFrameEvidence Capture(
        Window window,
        ThumbnailViewerControl viewer,
        Border witness,
        string outputDirectory)
    {
        var scroller = viewer.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Unflushed routed-wheel probe cannot find the actual ScrollViewer.");
        var point = scroller.TranslatePoint(
            new Point(
                scroller.Bounds.Width / 2,
                scroller.Bounds.Height / 2),
            window)
            ?? throw new InvalidOperationException(
                "Unflushed routed-wheel probe cannot locate target in window coordinates.");

        Directory.CreateDirectory(outputDirectory);
        // Use only stable externally callable Avalonia routed-event
        // surfaces. The raw platform input callback is inaccessible in
        // the Avalonia NuGet reference assembly even though its source
        // definition is visible in upstream internals.
        var target = window.InputHitTest(point) as InputElement
            ?? throw new InvalidOperationException(
                "Unflushed wheel probe found no hit-tested input control.");

        // Read the prior frame WITHOUT the headless helper's
        // pre/post auto-render loop so we can detect when the new tick
        // only returned an unchanged/stale framebuffer.
        var beforePath = Path.Combine(
            outputDirectory, "raw-before-input.png");
        using (var before = window.GetLastRenderedFrame()
            ?? throw new InvalidOperationException(
                "Unflushed routed-wheel probe lacks a pre-input raster baseline."))
        {
            before.Save(beforePath);
        }

        // The witness occupies only the top-right 16x16 pixels, outside
        // the bottom-row seven-column color samples. It lets us distinguish
        // a stale framebuffer from a new raster when all thumbnails are
        // visually identical. It does not assert a Windows GPU present.
        var preInputWitnessValid = HasWitnessColor(
            beforePath, expectChanged: false);
        if (!preInputWitnessValid)
        {
            throw new InvalidOperationException(
                "Raw-wheel diagnostic has no pre-input green witness raster.");
        }

        var beforeCount = viewer.RoutedWheelEventCount;
        var beforeOffset = scroller.Offset.Y;
        var previousDirection = viewer.CurrentScrollIntentDirection;

        // Raise the exact PointerWheelChanged routed event from
        // the hit-tested descendant, so the tunnel handler on the
        // viewer and the ScrollContentPresenter on bubble both run.
        // Crucially this avoids HeadlessWindowExtensions.MouseWheel's
        // RunJobsAndRender loop, while avoiding inaccessible raw APIs.
        // Four wheel units are aggregated into one event to challenge
        // the +198px direct-offset case.
        using var pointer = new Pointer(
            Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        target.RaiseEvent(new PointerWheelEventArgs(
            target, pointer, window, point, 0,
            new PointerPointProperties(),
            KeyModifiers.None,
            new Vector(0, -4)));

        var events = viewer.RoutedWheelEventCount - beforeCount;
        var moved = scroller.Offset.Y - beforeOffset;
        var direction = viewer.CurrentScrollIntentDirection;
        var immediateViewport = viewer.ViewportReadiness;
        if (events != 1 || moved <= 0 || direction != 1)
        {
            throw new InvalidOperationException(
                $"Unflushed routed-wheel event not delivered: events={events}, "
                + $"offsetDelta={moved:F1}, direction={direction}.");
        }

        // Change the diagnostic-only witness AFTER the wheel handler has
        // accepted real routed input. This color change is not sufficient
        // to prove a scroll-visible frame by itself, but it guarantees a
        // newly painted Skia raster differs from the flat blue fixture.
        witness.Background = Brushes.Magenta;

        // Intentionally do NOT run queued dispatcher jobs before the
        // first checkpoint. A single timer tick can legitimately return
        // a stale Skia framebuffer while layout/compositor jobs are queued.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var firstPath = Path.Combine(
            outputDirectory, "raw-first-explicit-tick.png");
        using (var first = window.GetLastRenderedFrame()
            ?? throw new InvalidOperationException(
                "Headless first unflushed routed-wheel render tick produced no frame."))
        {
            first.Save(firstPath);
        }

        var firstTickChangedRaster =
            !SHA256.HashData(File.ReadAllBytes(beforePath))
                .AsSpan()
                .SequenceEqual(
                    SHA256.HashData(File.ReadAllBytes(firstPath)));
        var firstAudit = RenderedFrameTileAudit.Inspect(
            firstPath, viewer.Columns);
        var firstTickWitnessChanged = HasWitnessColor(
            firstPath, expectChanged: true);

        // The pinned Avalonia helper normally drains UI dispatcher work
        // before rendering. Observe the first UPDATED post-dispatch raster
        // separately. Each pass is one explicit dispatcher drain followed
        // by exactly one render tick; never treat the preceding unchanged
        // framebuffer as first-frame proof, or silently loop to stability.
        const int maxPostDispatchPasses = 3;
        var postDispatchPath = Path.Combine(
            outputDirectory, "raw-first-post-dispatch-render.png");
        var postDispatchPasses = 0;
        var postDispatchWitnessChanged = false;
        for (var pass = 1; pass <= maxPostDispatchPasses; pass++)
        {
            postDispatchPasses = pass;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var postDispatch = window.GetLastRenderedFrame()
                ?? throw new InvalidOperationException(
                    "Post-dispatch headless tick produced no raster."))
            {
                postDispatch.Save(postDispatchPath);
            }

            postDispatchWitnessChanged = HasWitnessColor(
                postDispatchPath, expectChanged: true);
            if (postDispatchWitnessChanged)
            {
                break;
            }
        }

        var postDispatchRasterChanged =
            !SHA256.HashData(File.ReadAllBytes(beforePath))
                .AsSpan()
                .SequenceEqual(SHA256.HashData(
                    File.ReadAllBytes(postDispatchPath)));
        var postDispatchAudit = RenderedFrameTileAudit.Inspect(
            postDispatchPath, viewer.Columns);

        // For contrast, measure Avalonia's normal fully flushed
        // headless raster. It may advance up to 10 dispatcher/timer
        // passes: do NOT describe this as the first displayed frame.
        Dispatcher.UIThread.RunJobs();
        var settledPath = Path.Combine(
            outputDirectory, "raw-after-headless-flush.png");
        using (var settled = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException(
                "Headless flush produced no raster frame."))
        {
            settled.Save(settledPath);
        }

        var settledAudit = RenderedFrameTileAudit.Inspect(
            settledPath, viewer.Columns);
        if (firstAudit.SampledColumns != 7
            || firstAudit.OtherSamples != 0
            || postDispatchAudit.SampledColumns != 7
            || postDispatchAudit.OtherSamples != 0
            || settledAudit.SampledColumns != 7
            || settledAudit.OtherSamples != 0
            || !HasWitnessColor(settledPath, expectChanged: true))
        {
            throw new InvalidOperationException(
                "Raw-wheel Skia raster did not match the known blue/dark fixture.");
        }

        return new UnflushedRoutedWheelFrameEvidence(
            events,
            moved,
            previousDirection,
            direction,
            immediateViewport.UnreadyTiles,
            firstTickChangedRaster,
            preInputWitnessValid,
            firstTickWitnessChanged,
            postDispatchPasses,
            postDispatchWitnessChanged,
            postDispatchRasterChanged,
            firstAudit,
            postDispatchAudit,
            settledAudit);
    }

    // Check a pixel strictly inside the fixed top-right overlay. A
    // hash-only comparison is ambiguous for identical blue tile fixtures.
    private static bool HasWitnessColor(
        string pngPath, bool expectChanged)
    {
        using var image = SKBitmap.Decode(pngPath)
            ?? throw new InvalidOperationException(
                "Could not decode compositor witness raster.");
        var sample = image.GetPixel(image.Width - 8, 8);
        return expectChanged
            ? sample.Red >= 200 && sample.Blue >= 200
                && sample.Green <= 80
            : sample.Green >= 200 && sample.Red <= 80
                && sample.Blue <= 80;
    }
}
