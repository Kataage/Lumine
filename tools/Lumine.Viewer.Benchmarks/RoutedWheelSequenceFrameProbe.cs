using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumine.Viewer;

namespace Lumine.Viewer.Benchmarks;

// Ten distinct public routed pointer-wheel events. Unlike the convenience
// MouseWheel helper, each explicit framebuffer is captured after at most
// three dispatcher/render pairs, never after an unbounded implicit flush.
// Headless Skia evidence is not a physical Windows GPU-present fence.
internal readonly record struct RoutedWheelSequenceEvidence(
    int Steps,
    int ForwardSteps,
    int ReverseSteps,
    int DirectionChanges,
    int MaxImmediateUnready,
    int MaxRenderedUnready,
    int MinChangedSamples,
    int MaxPasses,
    long NewSourceRequests);

internal static class RoutedWheelSequenceFrameProbe
{
    internal static RoutedWheelSequenceEvidence Capture(
        Window window,
        ThumbnailViewerControl viewer,
        string outputDirectory)
    {
        var scroller = viewer.GetVisualDescendants()
            .OfType<ScrollViewer>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Sequence probe requires a mounted ScrollViewer.");
        var point = scroller.TranslatePoint(
            new Point(scroller.Bounds.Width / 2,
                scroller.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException(
                "Sequence probe requires a window-relative wheel point.");
        var target = window.InputHitTest(point) as InputElement
            ?? throw new InvalidOperationException(
                "Sequence probe failed hit testing.");

        Directory.CreateDirectory(outputDirectory);
        var beforeImage = Path.Combine(outputDirectory, "sequence-before.png");
        using (var baseline = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException(
                "Sequence probe has no pre-input Skia raster."))
        {
            baseline.Save(beforeImage);
        }

        // 4 forward, 4 reverse, 2 forward. No synthetic rest between events.
        // Each event must move exactly one normal wheel notch (~50px).
        double[] deltas = [-1, -1, -1, -1, 1, 1, 1, 1, -1, -1];
        using var pointer = new Pointer(
            Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var beforeRequests = viewer.Diagnostics.ThumbnailRequests;
        var initialOffset = scroller.Offset.Y;
        var lastDirection = 0;
        var directionChanges = 0;
        var forward = 0;
        var reverse = 0;
        var maxImmediate = 0;
        var maxRendered = 0;
        var minChanged = int.MaxValue;
        var maxPasses = 0;

        for (var i = 0; i < deltas.Length; i++)
        {
            var direction = deltas[i] < 0 ? 1 : -1;
            if (direction > 0)
            {
                forward++;
            }
            else
            {
                reverse++;
            }
            if (lastDirection != 0 && lastDirection != direction)
            {
                directionChanges++;
            }
            lastDirection = direction;

            var eventsBefore = viewer.RoutedWheelEventCount;
            var offsetBefore = scroller.Offset.Y;
            target.RaiseEvent(new PointerWheelEventArgs(
                target, pointer, window, point, 0,
                new PointerPointProperties(), KeyModifiers.None,
                new Vector(0, deltas[i])));
            var newEvents = viewer.RoutedWheelEventCount - eventsBefore;
            var moved = scroller.Offset.Y - offsetBefore;
            if (newEvents != 1
                || moved * direction <= 0
                || viewer.CurrentScrollIntentDirection != direction)
            {
                throw new InvalidOperationException(
                    $"Wheel sequence event {i + 1}: count={newEvents}, "
                    + $"offset={moved:F1}, "
                    + $"intent={viewer.CurrentScrollIntentDirection}, "
                    + $"expected={direction}.");
            }

            var immediate = viewer.ViewportReadiness.UnreadyTiles;
            maxImmediate = Math.Max(maxImmediate, immediate);
            var name = direction > 0 ? "forward" : "reverse";
            var path = Path.Combine(
                outputDirectory, $"sequence-{i + 1:D2}-{name}.png");
            var changed = 0;
            var passes = 0;
            for (var pass = 1; pass <= 3; pass++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using (var frame = window.GetLastRenderedFrame()
                    ?? throw new InvalidOperationException(
                        "Wheel sequence produced no Skia raster."))
                {
                    frame.Save(path);
                }
                changed = UnflushedRoutedWheelFirstFrameProbe
                    .CountChangedViewerSamples(beforeImage, path);
                passes = pass;
                if (changed >= 64)
                {
                    break;
                }
            }
            if (changed < 64)
            {
                throw new InvalidOperationException(
                    $"Wheel sequence {i + 1} {name} remained stale "
                    + $"after {passes} render ticks: {changed} changed samples.");
            }

            var rendered = viewer.ViewportReadiness.UnreadyTiles;
            maxRendered = Math.Max(maxRendered, rendered);
            maxPasses = Math.Max(maxPasses, passes);
            minChanged = Math.Min(minChanged, changed);
            Console.WriteLine(
                $"Wheel sequence {i + 1}/{deltas.Length}: "
                + $"direction={name}, offset={moved:F1}px, "
                + $"unready immediate/rendered={immediate}/{rendered}, "
                + $"changed samples={changed}, passes={passes}.");
            beforeImage = path;
        }

        var finalMove = scroller.Offset.Y - initialOffset;
        if (forward != 6 || reverse != 4
            || directionChanges != 2
            || Math.Abs(finalMove - 100) > 1)
        {
            throw new InvalidOperationException(
                $"Wheel sequence ended at unexpected location: "
                + $"forward={forward}, reverse={reverse}, "
                + $"switches={directionChanges}, moved={finalMove:F1}px.");
        }

        return new RoutedWheelSequenceEvidence(
            deltas.Length, forward, reverse, directionChanges,
            maxImmediate, maxRendered, minChanged, maxPasses,
            viewer.Diagnostics.ThumbnailRequests - beforeRequests);
    }
}
