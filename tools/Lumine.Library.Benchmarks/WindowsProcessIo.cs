using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lumine.Library.Benchmarks;

/// <summary>
/// Windows-only process-level I/O accounting for diagnosing SQLite COMMIT
/// stalls. Counts include all I/O made by this process, not only the WAL,
/// and do not measure device service time or individual checkpoint calls.
/// </summary>
internal static partial class WindowsProcessIo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeIoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    internal readonly record struct Snapshot(
        ulong ReadOperations,
        ulong WriteOperations,
        ulong ReadBytes,
        ulong WriteBytes);

    [LibraryImport("kernel32.dll", EntryPoint = "GetProcessIoCounters",
        SetLastError = true)]
    private static partial int GetProcessIoCounters(
        nint processHandle, out NativeIoCounters counters);

    internal static bool TryCapture(Process process, out Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(process);
        snapshot = default;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (GetProcessIoCounters(process.Handle, out var counters) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(),
                "Windows GetProcessIoCounters failed during benchmark diagnostics.");
        }

        snapshot = new Snapshot(
            counters.ReadOperationCount,
            counters.WriteOperationCount,
            counters.ReadTransferCount,
            counters.WriteTransferCount);
        return true;
    }

    internal static long Delta(ulong current, ulong previous)
    {
        if (current < previous)
        {
            throw new InvalidOperationException(
                "Windows process I/O counters unexpectedly decreased.");
        }

        return checked((long)(current - previous));
    }
}
