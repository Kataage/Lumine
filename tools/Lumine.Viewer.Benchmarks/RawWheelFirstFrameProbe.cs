using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumine.Viewer;

namespace Lumine.Viewer.Benchmarks;

// Opt-in experimental first-tick raster evidence. Avalonia's documented
// Window.MouseWheel helper calls RunJobsAndRender before AND after input.
// Using that helper cannot inspect the first explicit rendering tick;
// the public platform Input callback allows a raw wheel to be routed
// synchronously, followed by one explicit headless render tick. This
// deliberately never runs in the baseline timing/performance sample.
internal readonly record struct RawWheelFrameEvidence(
    long RoutedEvents,
    double OffsetDeltaPixels,
    int DirectionBefore,
    int DirectionAfter,
    int UnreadyImmediatelyAfterInput,
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
        var platformInput = window.PlatformImpl?.Input
            ?? throw new InvalidOperationException(
                "Raw-wheel probe cannot access public platform input callback.");

        var beforeCount = viewer.RoutedWheelEventCount;
        var beforeOffset = scroller.Offset.Y;
        var previousDirection = viewer.CurrentScrollIntentDirection;

        // Avalonia 12.1.3's HeadlessWindowImpl.MouseWheel ultimately
        // constructs a RawMouseWheelEventArgs and forwards it to its
        // public ITopLevelImpl.Input callback. We repeat that boundary
        // without calling the *convenience helper's* implicit render
        // stabilization loop. This does not bypass routed input.
        // Use 4 wheel units atomically (nominally 4x50px) to challenge
        // the single large-offset case with a real input route.
        using var mouse = new MouseDevice();
        platformInput(new RawMouseWheelEventArgs(
            mouse,
            timestamp: 0,
            window,
            point,
            new Vector(0, -4),
            RawInputModifiers.None));

        var events = viewer.RoutedWheelEventCount - beforeCount;
        var moved = scroller.Offset.Y - beforeOffset;
        var direction = viewer.CurrentScrollIntentDirection;
        var immediateViewport = viewer.ViewportReadiness;
        if (events != 1 || moved <= 0 || direction != 1)
        {
            throw new InvalidOperationException(
                $"Raw-wheel event not routed: events={events}, "
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
            firstAudit,
            settledAudit);
    }
}
