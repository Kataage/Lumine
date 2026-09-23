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

        var version =
            $"{global::NetVips.NetVips.Version(0)}." +
            $"{global::NetVips.NetVips.Version(1)}." +
            $"{global::NetVips.NetVips.Version(2)}";

        return new VipsProbeResult(version, true);
    }
}
