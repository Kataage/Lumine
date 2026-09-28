using System.Runtime.InteropServices;
using Lumine.Core;

namespace Lumine.Image;

public static class VipsRuntimePolicy
{
    public const int ThumbnailOperationLimit = 0;
    public const int ThumbnailCachedFileLimit = 0;

    private static readonly object ConfigurationGate = new();
    private static int _configured;
    private static int _configuredConcurrency;
    private static long _configuredTrackedMemoryBytes;

    public static int ThumbnailVipsConcurrency
    {
        get
        {
            lock (ConfigurationGate)
            {
                return _configured != 0
                    ? _configuredConcurrency
                    : CoreResourcePolicy.Default.VipsConcurrency;
            }
        }
    }

    public static ulong ThumbnailTrackedMemoryLimitBytes
    {
        get
        {
            lock (ConfigurationGate)
            {
                var value = _configured != 0
                    ? _configuredTrackedMemoryBytes
                    : CoreResourcePolicy.Default.VipsTrackedMemoryLimitBytes;
                return checked((ulong)value);
            }
        }
    }

    public static ulong DiscThresholdBytes
    {
        get
        {
            EnsureConfigured();
            return VipsNative.GetDiscThreshold();
        }
    }

    public static void EnsureConfigured(
        CoreResourcePolicy? resourcePolicy = null)
    {
        lock (ConfigurationGate)
        {
            if (_configured != 0)
            {
                if (resourcePolicy is not null
                    && (_configuredConcurrency
                        != resourcePolicy.VipsConcurrency
                        || _configuredTrackedMemoryBytes
                        != resourcePolicy.VipsTrackedMemoryLimitBytes))
                {
                    throw new InvalidOperationException(
                        "libvips process-global resource policy was already configured with different values.");
                }

                return;
            }

            var policy =
                resourcePolicy ?? CoreResourcePolicy.Default;

            // Lumine owns its persistent thumbnail cache. Keeping source/cache file
            // operations alive in libvips' process-global operation cache only adds
            // hidden file handles and makes Windows prune/shutdown less predictable.
            global::NetVips.Cache.Max = ThumbnailOperationLimit;
            global::NetVips.Cache.MaxMem =
                checked((ulong)policy.VipsTrackedMemoryLimitBytes);
            global::NetVips.Cache.MaxFiles = ThumbnailCachedFileLimit;
            global::NetVips.NetVips.Concurrency = policy.VipsConcurrency;

            _configuredConcurrency = policy.VipsConcurrency;
            _configuredTrackedMemoryBytes =
                policy.VipsTrackedMemoryLimitBytes;

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
