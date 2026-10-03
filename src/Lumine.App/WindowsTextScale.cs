using System.Globalization;
using Microsoft.Win32;

namespace Lumine.App;

internal static class WindowsTextScale
{
    private const string EnvironmentOverride =
        "LUMINE_TEXT_SCALE";
    private const string AccessibilityRegistryPath =
        @"Software\Microsoft\Accessibility";
    private const string TextScaleFactorValue =
        "TextScaleFactor";
    private const double WindowsBaseline = 96d;

    public static double Resolve()
    {
        var environment =
            Environment.GetEnvironmentVariable(
                EnvironmentOverride);
        if (double.TryParse(
                environment,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var overrideFactor)
            && double.IsFinite(overrideFactor)
            && overrideFactor > 0)
        {
            return Math.Clamp(
                overrideFactor,
                1d,
                2.25d);
        }

        if (!OperatingSystem.IsWindows())
        {
            return 1d;
        }

        try
        {
            using var key =
                Registry.CurrentUser.OpenSubKey(
                    AccessibilityRegistryPath);
            var value =
                key?.GetValue(
                    TextScaleFactorValue);

            var raw =
                value switch
                {
                    int integer => integer,
                    long integer => integer,
                    string text
                        when double.TryParse(
                            text,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var parsed) =>
                        parsed,
                    _ => WindowsBaseline
                };

            if (!double.IsFinite(raw)
                || raw <= 0)
            {
                return 1d;
            }

            // Windows stores the accessibility text scale with 96 as the
            // baseline. Microsoft presets include 120 and 144; clamp larger
            // values to Lumine's supported 225% acceptance ceiling.
            return Math.Clamp(
                raw / WindowsBaseline,
                1d,
                2.25d);
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException
                or System.Security.SecurityException
                or IOException)
        {
            return 1d;
        }
    }
}
