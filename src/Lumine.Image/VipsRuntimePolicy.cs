namespace Lumine.Image;

public static class VipsRuntimePolicy
{
    public const ulong ThumbnailTrackedMemoryLimitBytes = 64UL * 1024 * 1024;
    public const int ThumbnailOperationLimit = 256;
    public const int ThumbnailCachedFileLimit = 0;

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

            // Publish configured only after every process-global setting succeeds.
            Volatile.Write(ref _configured, 1);
        }
    }
}
