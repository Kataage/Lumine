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
    public const string ControlSurface = "#1C1C20";
    public const string ControlHover = "#242429";
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
    public const string Focus = "#D4D4D8";
    public const string Selection = "#E4E4E7";
}
