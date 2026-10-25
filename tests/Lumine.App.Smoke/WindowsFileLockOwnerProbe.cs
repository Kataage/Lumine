using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

// Failure-only Windows App Smoke forensics. This is NOT a runtime dependency.
// Restart Manager enumerates applications/services associated with a file,
// but it cannot attribute a specific SQLite connection or guarantee that
// all possible kernel handle holders are listed. Empty = inconclusive.
internal static class WindowsFileLockOwnerProbe
{
    private const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public uint StartTimeLow;
        public uint StartTimeHigh;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string ServiceShortName;

        public int AppType;
        public uint AppStatus;
        public uint TerminalSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

    internal sealed record Owner(int ProcessId, string AppName);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(
        out uint handle,
        int flags,
        StringBuilder key);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint handle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint handle,
        uint fileCount,
        [In] string[] fileNames,
        uint applicationCount,
        IntPtr applications,
        uint serviceCount,
        IntPtr services);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint handle,
        out uint needed,
        ref uint capacity,
        [In, Out] RmProcessInfo[]? affected,
        ref uint rebootReasons);

    internal static IReadOnlyList<Owner> GetOwners(string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Restart Manager file diagnostics are Windows-only.");
        }

        var key = new StringBuilder(capacity: 33);
        var status = RmStartSession(out var session, 0, key);
        Check(status, "RmStartSession");
        try
        {
            status = RmRegisterResources(
                session, 1, [Path.GetFullPath(filePath)],
                0, IntPtr.Zero, 0, IntPtr.Zero);
            Check(status, "RmRegisterResources");

            uint capacity = 0;
            uint rebootReasons = 0;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var entries = capacity == 0
                    ? null
                    : new RmProcessInfo[checked((int)capacity)];
                status = RmGetList(
                    session, out var needed, ref capacity,
                    entries, ref rebootReasons);
                if (status == 0)
                {
                    return entries is null
                        ? []
                        : entries.Take(checked((int)capacity))
                            .Select(x => new Owner(
                                x.Process.ProcessId,
                                x.AppName ?? string.Empty))
                            .ToArray();
                }

                if (status != ErrorMoreData)
                {
                    Check(status, "RmGetList");
                }

                capacity = needed;
            }

            throw new Win32Exception(
                ErrorMoreData,
                "RmGetList owner set kept changing during four bounded attempts.");
        }
        finally
        {
            _ = RmEndSession(session);
        }
    }

    internal static string DescribeOwners(string filePath)
    {
        try
        {
            var owners = GetOwners(filePath);
            if (owners.Count == 0)
            {
                return "none-reported(inconclusive)";
            }

            return string.Join(
                "; ",
                owners
                    .OrderBy(x => x.ProcessId)
                    .Select(x =>
                        $"pid={x.ProcessId},app={x.AppName}"));
        }
        catch (Exception exception)
            when (exception is Win32Exception
                or DllNotFoundException
                or EntryPointNotFoundException
                or InvalidOperationException)
        {
            return $"probe-error({exception.GetType().Name}):{exception.Message}";
        }
    }

    private static void Check(int status, string function)
    {
        if (status != 0)
        {
            throw new Win32Exception(
                status,
                $"{function} returned Win32 error {status}.");
        }
    }
}
