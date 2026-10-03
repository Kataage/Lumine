namespace Lumine.Core;

/// <summary>
/// Canonical product palette values shared by the App and Viewer assemblies.
/// Keep raw color values here so architecture boundaries cannot create
/// independent copies of the Lumine visual language.
/// </summary>
public static class LumineVisualPalette
{
    public const string Background = "#09090B";
    public const string Surface = "#0D0D10";
    public const string SurfaceRaised = "#151518";
    public const string InteractionNeutral = "#1C1C20";
    public const string InteractionHover = "#28282E";
    public const string InteractionPressed = "#34343C";
    public const string InteractionSelected = "#303037";
    public const string InteractionSelectedHover = "#3A3A43";
    public const string InteractionFocus = "#D4D4D8";
    public const string InteractionDisabled = "#151518";
    public const string InteractionDangerHover = "#EF6868";
    public const string InteractionDangerPressed = "#D95757";
    public const string ControlSurface = InteractionNeutral;
    public const string ControlHover = InteractionHover;
    public const string Border = "#29292F";
    public const string ViewerBorder = "#3F3F46";
    public const string BorderStrong = "#71717A";
    public const string Foreground = "#FAFAFA";
    public const string MutedForeground = "#A1A1AA";
    public const string ViewerMutedForeground = "#D4D4D8";
    public const string Accent = "#FAFAFA";
    public const string AccentMuted = "#222227";
    public const string Danger = "#F87171";
    public const string Warning = "#FBBF24";
    public const string Focus = InteractionFocus;
    public const string Selection = "#E4E4E7";
    public const string ModalScrim = "#BE000000";
    public const string Overlay = "#D618181B";
    public const string OverlaySoft = "#B018181B";
    public const string CaptionGradientTransparent = "#00000000";
    public const string CaptionGradientMiddle = "#46000000";
    public const string CaptionGradientEnd = "#DC000000";
}


/// <summary>
/// Shared visual metrics that must remain identical across App and Viewer.
/// The text scale is configured once from the Windows accessibility setting
/// before product controls are constructed.
/// </summary>
public static class LumineVisualMetrics
{
    private static double _textScaleFactor = 1d;

    public static double TextScaleFactor =>
        Volatile.Read(ref _textScaleFactor);

    public static void ConfigureTextScaleFactor(
        double factor)
    {
        if (!double.IsFinite(factor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(factor));
        }

        Volatile.Write(
            ref _textScaleFactor,
            Math.Clamp(factor, 1d, 2.25d));
    }
}
