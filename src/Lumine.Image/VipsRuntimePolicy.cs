using System.Runtime.InteropServices;

namespace Lumine.Image;

public static class VipsRuntimePolicy
{
    public const ulong ThumbnailTrackedMemoryLimitBytes = 64UL * 1024 * 1024;
    public const int ThumbnailOperationLimit = 0;
    public const int ThumbnailCachedFileLimit = 0;

    public static int ThumbnailVipsConcurrency { get; } =
        Math.Clamp(
            (Environment.ProcessorCount + ThumbnailPipelineOptions.DefaultWorkerCount - 1)
                / ThumbnailPipelineOptions.DefaultWorkerCount,
            1,
            4);

    public static ulong DiscThresholdBytes
    {
        get
        {
            EnsureConfigured();
            return VipsNative.GetDiscThreshold();
        }
    }

    private static readonly object ConfigurationGate = new();
    private static int _configured;

    public static void EnsureConfigured()
    {
        if (Volatile.Read(ref _configured) != 0)
        {
            return;
        }

        lock (ConfigurationGate)
        {
            if (_configured != 0)
            {
                return;
            }

            // Lumine owns its persistent thumbnail cache. Keeping source/cache file
            // operations alive in libvips' process-global operation cache only adds
            // hidden file handles and makes Windows prune/shutdown less predictable.
            global::NetVips.Cache.Max = ThumbnailOperationLimit;
            global::NetVips.Cache.MaxMem = ThumbnailTrackedMemoryLimitBytes;
            global::NetVips.Cache.MaxFiles = ThumbnailCachedFileLimit;
            global::NetVips.NetVips.Concurrency = ThumbnailVipsConcurrency;

            // Publish configured only after every process-global setting succeeds.
            Volatile.Write(ref _configured, 1);
        }
    }

    private static class VipsNative
    {
        [DllImport(
            "libvips-42.dll",
            EntryPoint = "vips_get_disc_threshold",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong GetDiscThreshold();
    }
}
