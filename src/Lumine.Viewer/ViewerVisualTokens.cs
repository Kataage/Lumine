using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lumine.Viewer;

internal static class ViewerVisualTokens
{
    public static readonly Color StageColor = Color.Parse("#09090B");
    public static readonly Color SurfaceColor = Color.Parse("#151518");
    public static readonly Color SurfaceRaisedColor = Color.Parse("#1C1C20");
    public static readonly Color BorderColor = Color.Parse("#3F3F46");
    public static readonly Color BorderStrongColor = Color.Parse("#71717A");
    public static readonly Color ForegroundColor = Color.Parse("#FAFAFA");
    public static readonly Color MutedForegroundColor = Color.Parse("#D4D4D8");
    public static readonly Color SelectionColor = Color.Parse("#E4E4E7");

    public static readonly IBrush Stage = new SolidColorBrush(StageColor);
    public static readonly IBrush Surface = new SolidColorBrush(SurfaceColor);
    public static readonly IBrush SurfaceRaised = new SolidColorBrush(SurfaceRaisedColor);
    public static readonly IBrush Border = new SolidColorBrush(BorderColor);
    public static readonly IBrush BorderStrong = new SolidColorBrush(BorderStrongColor);
    public static readonly IBrush Foreground = new SolidColorBrush(ForegroundColor);
    public static readonly IBrush MutedForeground = new SolidColorBrush(MutedForegroundColor);
    public static readonly IBrush Selection = new SolidColorBrush(SelectionColor);
    public static readonly IBrush Overlay =
        new SolidColorBrush(Color.FromArgb(214, 24, 24, 27));
    public static readonly IBrush OverlaySoft =
        new SolidColorBrush(Color.FromArgb(176, 24, 24, 27));

    public static readonly IBrush CaptionGradient =
        new LinearGradientBrush
        {
            StartPoint =
                new RelativePoint(
                    0,
                    0,
                    RelativeUnit.Relative),
            EndPoint =
                new RelativePoint(
                    0,
                    1,
                    RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(
                    Color.FromArgb(0, 0, 0, 0),
                    0),
                new GradientStop(
                    Color.FromArgb(70, 0, 0, 0),
                    0.35),
                new GradientStop(
                    Color.FromArgb(220, 0, 0, 0),
                    1)
            ]
        };

    public const double BodyFontSize = 14;
    public const double CaptionFontSize = 12;

    public static void Name(
        Control control,
        string accessibleName,
        string? automationId = null,
        string? acceleratorKey = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessibleName);

        AutomationProperties.SetName(control, accessibleName);
        if (!string.IsNullOrWhiteSpace(automationId))
        {
            AutomationProperties.SetAutomationId(control, automationId);
        }

        if (!string.IsNullOrWhiteSpace(acceleratorKey))
        {
            AutomationProperties.SetAcceleratorKey(
                control,
                acceleratorKey);
        }
    }
}
