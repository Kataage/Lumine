using System.Runtime.InteropServices;

namespace Lumine.Core;

public static class FoundationInfo
{
    public const string ProductName = "Lumine";
    public const int ArchitectureVersion = 2;

    public static string RuntimeDescription =>
        $"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}";
}
