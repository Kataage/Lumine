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
    private const double WindowsPercentBaseline = 100d;

    internal static double NormalizeRegistryValue(
        double raw)
    {
        if (!double.IsFinite(raw)
            || raw <= 0)
        {
            return 1d;
        }

        // HKCU\Software\Microsoft\Accessibility\TextScaleFactor stores a
        // percentage: 100 is the default 100% text size and 225 is 225%.
        return Math.Clamp(
            raw / WindowsPercentBaseline,
            1d,
            2.25d);
    }

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
                    _ => WindowsPercentBaseline
                };

            if (!double.IsFinite(raw)
                || raw <= 0)
            {
                return 1d;
            }

            return NormalizeRegistryValue(raw);
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
