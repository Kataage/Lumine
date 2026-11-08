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

    // True only after Windows' loader has permanently pinned the actual
    // native libvips module (GetModuleHandleExW PIN + FROM_ADDRESS).
    // This is stronger than merely keeping a LoadLibrary reference count.
    public static bool IsProcessLifetimePinned
    {
        get
        {
            lock (ConfigurationGate)
            {
                return OperatingSystem.IsWindows()
                    && _processLifetimeHandle != 0;
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
            // The Windows loader's explicit permanent PIN (not a leaked
            // LoadLibrary reference) keeps libvips mapped until process exit.
            // Do not try to release it during managed/native teardown.
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

        // The 2026-11-08 physical WER dump captured an *execute*
        // access violation (ExceptionInformation[0] = 8) at an address
        // inside libvips-42.dll's UNLOADED image. Keeping one ordinary
        // NativeLibrary.Load reference was not sufficient in practice.
        //
        // The Windows loader's permanent pin is guaranteed to survive
        // any subsequent FreeLibrary calls until the process terminates.
        // Use the export's actual address instead of just a module name:
        // this pins the exact DLL whose code NetVips will call.
        var loadedHandle =
            File.Exists(localPath)
                ? NativeLibrary.Load(localPath)
                : NativeLibrary.Load("libvips-42.dll");

        try
        {
            var exportAddress =
                NativeLibrary.GetExport(
                    loadedHandle,
                    "vips_get_disc_threshold");

            const uint getModuleHandleExPin = 0x00000001;
            const uint getModuleHandleExFromAddress = 0x00000004;
            var pinned = GetModuleHandleExW(
                getModuleHandleExPin | getModuleHandleExFromAddress,
                exportAddress,
                out var pinnedHandle);
            var win32Error = Marshal.GetLastPInvokeError();

            if (!pinned
                || pinnedHandle == 0
                || pinnedHandle != loadedHandle)
            {
                throw new InvalidOperationException(
                    "Could not permanently pin the exact libvips-42.dll "
                    + $"image for process lifetime (Win32 error {win32Error}).");
            }

            _processLifetimeHandle = pinnedHandle;
        }
        finally
        {
            // Balance the initial load; GET_MODULE_HANDLE_EX_FLAG_PIN
            // guarantees the image remains mapped even after this Free.
            NativeLibrary.Free(loadedHandle);
        }
    }

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetModuleHandleExW",
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetModuleHandleExW(
        uint flags,
        nint addressInModule,
        out nint moduleHandle);

    private static class VipsNative
    {
        [DllImport(
            "libvips-42.dll",
            EntryPoint = "vips_get_disc_threshold",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong GetDiscThreshold();
    }
}
