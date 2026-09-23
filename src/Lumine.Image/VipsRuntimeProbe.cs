namespace Lumine.Image;

public readonly record struct VipsProbeResult(string Version, bool Initialized);

public static class VipsRuntimeProbe
{
    public static VipsProbeResult Probe()
    {
        if (!global::NetVips.ModuleInitializer.VipsInitialized)
        {
            throw new InvalidOperationException("libvips failed to initialize.");
        }

        var major = global::NetVips.NetVips.Version(0);
        var minor = global::NetVips.NetVips.Version(1);
        var patch = global::NetVips.NetVips.Version(2);
        var version = $"{major}.{minor}.{patch}";

        return new VipsProbeResult(version, true);
    }
}
