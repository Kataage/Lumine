using System.Runtime.InteropServices;
using Lumine.Core;

namespace Lumine.Image;

public static class VipsRuntimePolicy
{
    public const int ThumbnailOperationLimit = 0;
    public const int ThumbnailCachedFileLimit = 0;

    private static readonly object ConfigurationGate = new();
    private static int _configured;
    private static int _shutdown;
    private static nint _processLifetimeHandle;
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

            EnsureProcessLifetimePinned();

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

    public static void ShutdownProcessLifetime()
    {
        lock (ConfigurationGate)
        {
            if (_configured == 0
                || Interlocked.Exchange(ref _shutdown, 1) != 0)
            {
                return;
            }

            // libvips owns internal worker/background threads. Shut those down
            // explicitly after every Lumine image pipeline/object has been drained.
            // Keep the extra Windows module reference pinned until process exit:
            // NativeLibrary.Free is intentionally not called here. This prevents
            // any late native callback/finalizer from jumping into an unmapped
            // libvips image while the OS is still tearing the process down.
            global::NetVips.NetVips.Shutdown();
        }
    }

    private static void EnsureProcessLifetimePinned()
    {
        if (!OperatingSystem.IsWindows()
            || _processLifetimeHandle != 0)
        {
            return;
        }

        var localPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "libvips-42.dll");

        _processLifetimeHandle =
            File.Exists(localPath)
                ? NativeLibrary.Load(localPath)
                : NativeLibrary.Load("libvips-42.dll");

        if (_processLifetimeHandle == 0)
        {
            throw new DllNotFoundException(
                "Unable to pin libvips-42.dll for the Lumine process lifetime.");
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
