using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumine.Viewer;

namespace Lumine.Viewer.Benchmarks;

// Opt-in experimental first-tick raster evidence. Avalonia's documented
// Window.MouseWheel helper calls RunJobsAndRender before AND after input.
// Using that helper cannot inspect the first explicit rendering tick;
// the public PointerWheelEventArgs + InputElement.RaiseEvent API
// allows a routed wheel without the implicit render-flush helper. This
// deliberately never runs in the baseline timing/performance sample.
internal readonly record struct RawWheelFrameEvidence(
    long RoutedEvents,
    double OffsetDeltaPixels,
    int DirectionBefore,
    int DirectionAfter,
    int UnreadyImmediatelyAfterInput,
    bool FirstTickContainsUpdatedRaster,
    RenderedFrameTileAudit FirstTick,
    RenderedFrameTileAudit Settled);

internal static class RawWheelFirstFrameProbe
{
    internal static RawWheelFrameEvidence Capture(
        Window window,
        ThumbnailViewerControl viewer,
        string outputDirectory)
    {
        var scroller = viewer.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Raw-wheel probe cannot find the actual ScrollViewer.");
        var point = scroller.TranslatePoint(
            new Point(
                scroller.Bounds.Width / 2,
                scroller.Bounds.Height / 2),
            window)
            ?? throw new InvalidOperationException(
                "Raw-wheel probe cannot locate target in window coordinates.");

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
                "Raw-wheel probe lacks a pre-input raster baseline."))
        {
            before.Save(beforePath);
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

        // Intentionally do NOT call Dispatcher.RunJobs or the
        // Window.MouseWheel/CaptureRenderedFrame convenience helpers
        // before this checkpoint. Trigger one render timer tick and
        // read the frame *without* another dispatcher-flush loop.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var firstPath = Path.Combine(
            outputDirectory, "raw-first-explicit-tick.png");
        using (var first = window.GetLastRenderedFrame()
            ?? throw new InvalidOperationException(
                "Headless first raw-wheel render tick produced no frame."))
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
            || settledAudit.SampledColumns != 7
            || settledAudit.OtherSamples != 0)
        {
            throw new InvalidOperationException(
                "Raw-wheel Skia raster did not match the known blue/dark fixture.");
        }

        return new RawWheelFrameEvidence(
            events,
            moved,
            previousDirection,
            direction,
            immediateViewport.UnreadyTiles,
            firstTickChangedRaster,
            firstAudit,
            settledAudit);
    }
}
