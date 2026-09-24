using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lumine.Library;

public sealed class WindowsDirectoryChangeWatcher : IAsyncDisposable
{
    private const int BufferSize = 32 * 1024;

    private const uint FileListDirectory = 0x0001;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int ErrorIoPending = 997;
    private const int ErrorOperationAborted = 995;

    private const uint NotifyChangeFileName = 0x00000001;
    private const uint NotifyChangeDirName = 0x00000002;
    private const uint NotifyChangeSize = 0x00000008;
    private const uint NotifyChangeLastWrite = 0x00000010;
    private const uint NotifyChangeCreation = 0x00000040;

    private readonly string _rootPath;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _watchTask;
    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WindowsDirectoryChangeWatcher(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = LibraryPaths.NormalizeRoot(rootPath);
    }

    public async Task StartAsync(
        Action<IReadOnlyList<DirectoryChange>> publish,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publish);

        if (_watchTask is not null)
        {
            throw new InvalidOperationException("Watcher has already been started.");
        }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);

        _watchTask = Task.Factory.StartNew(
            () =>
            {
                try
                {
                    RunLoop(publish, linked.Token);
                }
                catch (Exception exception)
                {
                    _ready.TrySetException(exception);
                    throw;
                }
                finally
                {
                    linked.Dispose();
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task Completion =>
        _watchTask ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();

        if (_watchTask is not null)
        {
            try
            {
                await _watchTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _shutdown.Dispose();
    }

    internal static List<DirectoryChange> ParseBuffer(
        IntPtr buffer,
        uint bytes,
        DateTimeOffset observedAtUtc)
    {
        string? pendingRenameOld = null;
        return ParseBuffer(
            buffer,
            bytes,
            observedAtUtc,
            ref pendingRenameOld,
            flushPendingRename: true);
    }

    private static List<DirectoryChange> ParseBuffer(
        IntPtr buffer,
        uint bytes,
        DateTimeOffset observedAtUtc,
        ref string? pendingRenameOld,
        bool flushPendingRename)
    {
        if (bytes == 0)
        {
            return
            [
                new DirectoryChange(
                    DirectoryChangeKind.Overflow,
                    string.Empty,
                    null,
                    observedAtUtc)
            ];
        }

        var result = new List<DirectoryChange>();
        var offset = 0;

        while ((uint)offset < bytes)
        {
            var entry = IntPtr.Add(buffer, offset);
            var nextOffset = Marshal.ReadInt32(entry, 0);
            var action = Marshal.ReadInt32(entry, 4);
            var nameBytes = Marshal.ReadInt32(entry, 8);

            if (nameBytes < 0 || nameBytes % 2 != 0 || (uint)(offset + 12 + nameBytes) > bytes)
            {
                return
                [
                    new DirectoryChange(
                        DirectoryChangeKind.Overflow,
                        string.Empty,
                        null,
                        observedAtUtc)
                ];
            }

            var name = Marshal.PtrToStringUni(
                IntPtr.Add(entry, 12),
                nameBytes / 2);

            if (string.IsNullOrWhiteSpace(name))
            {
                return
                [
                    new DirectoryChange(
                        DirectoryChangeKind.Overflow,
                        string.Empty,
                        null,
                        observedAtUtc)
                ];
            }

            var relativePath = LibraryPaths.NormalizeRelativePath(name);

            if (pendingRenameOld is not null && action != 5)
            {
                result.Add(
                    new DirectoryChange(
                        DirectoryChangeKind.Removed,
                        pendingRenameOld,
                        null,
                        observedAtUtc));
                pendingRenameOld = null;
            }

            switch (action)
            {
                case 1:
                    result.Add(
                        new DirectoryChange(
                            DirectoryChangeKind.Added,
                            relativePath,
                            null,
                            observedAtUtc));
                    break;
                case 2:
                    result.Add(
                        new DirectoryChange(
                            DirectoryChangeKind.Removed,
                            relativePath,
                            null,
                            observedAtUtc));
                    break;
                case 3:
                    result.Add(
                        new DirectoryChange(
                            DirectoryChangeKind.Modified,
                            relativePath,
                            null,
                            observedAtUtc));
                    break;
                case 4:
                    pendingRenameOld = relativePath;
                    break;
                case 5:
                    if (pendingRenameOld is null)
                    {
                        result.Add(
                            new DirectoryChange(
                                DirectoryChangeKind.Added,
                                relativePath,
                                null,
                                observedAtUtc));
                    }
                    else
                    {
                        result.Add(
                            new DirectoryChange(
                                DirectoryChangeKind.Renamed,
                                relativePath,
                                pendingRenameOld,
                                observedAtUtc));
                        pendingRenameOld = null;
                    }

                    break;
                default:
                    return
                    [
                        new DirectoryChange(
                            DirectoryChangeKind.Overflow,
                            string.Empty,
                            null,
                            observedAtUtc)
                    ];
            }

            if (nextOffset == 0)
            {
                break;
            }

            if (nextOffset < 12 || (uint)(offset + nextOffset) > bytes)
            {
                return
                [
                    new DirectoryChange(
                        DirectoryChangeKind.Overflow,
                        string.Empty,
                        null,
                        observedAtUtc)
                ];
            }

            offset += nextOffset;
        }

        if (flushPendingRename && pendingRenameOld is not null)
        {
            result.Add(
                new DirectoryChange(
                    DirectoryChangeKind.Removed,
                    pendingRenameOld,
                    null,
                    observedAtUtc));
            pendingRenameOld = null;
        }

        return result;
    }

    private void RunLoop(
        Action<IReadOnlyList<DirectoryChange>> publish,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "ReadDirectoryChangesW watcher is Windows-only.");
        }

        using var handle = CreateFileW(
            _rootPath,
            FileListDirectory,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOverlapped,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Unable to open directory watcher handle for '{_rootPath}'.");
        }

        var buffer = Marshal.AllocHGlobal(BufferSize);
        var overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<OverlappedNative>());
        string? pendingRenameOld = null;

        try
        {
            using var completed = new EventWaitHandle(
                false,
                EventResetMode.ManualReset);

            var native = new OverlappedNative
            {
                EventHandle = completed.SafeWaitHandle.DangerousGetHandle()
            };

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                completed.Reset();
                Marshal.StructureToPtr(native, overlapped, false);

                var started = ReadDirectoryChangesW(
                    handle,
                    buffer,
                    BufferSize,
                    true,
                    NotifyChangeFileName
                    | NotifyChangeDirName
                    | NotifyChangeSize
                    | NotifyChangeLastWrite
                    | NotifyChangeCreation,
                    IntPtr.Zero,
                    overlapped,
                    IntPtr.Zero);

                if (!started)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ErrorIoPending)
                    {
                        throw new Win32Exception(
                            error,
                            "ReadDirectoryChangesW failed.");
                    }
                }

                // StartAsync is not considered ready until the first native
                // subtree read has actually been armed.
                _ready.TrySetResult();

                int wait;
                while (true)
                {
                    wait = WaitHandle.WaitAny(
                        [completed, cancellationToken.WaitHandle],
                        pendingRenameOld is null ? Timeout.Infinite : 100);

                    if (wait != WaitHandle.WaitTimeout)
                    {
                        break;
                    }

                    if (pendingRenameOld is not null)
                    {
                        publish(
                            [
                                new DirectoryChange(
                                    DirectoryChangeKind.Removed,
                                    pendingRenameOld,
                                    null,
                                    DateTimeOffset.UtcNow)
                            ]);
                        pendingRenameOld = null;
                    }
                }

                if (wait == 1)
                {
                    _ = CancelIoEx(handle, overlapped);
                    completed.WaitOne();

                    _ = GetOverlappedResult(
                        handle,
                        overlapped,
                        out _,
                        false);

                    throw new OperationCanceledException(cancellationToken);
                }

                if (!GetOverlappedResult(
                        handle,
                        overlapped,
                        out var bytes,
                        false))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorOperationAborted
                        && cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    throw new Win32Exception(
                        error,
                        "ReadDirectoryChangesW completion failed.");
                }

                publish(
                    ParseBuffer(
                        buffer,
                        bytes,
                        DateTimeOffset.UtcNow,
                        ref pendingRenameOld,
                        flushPendingRename: false));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(overlapped);
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OverlappedNative
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr EventHandle;
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadDirectoryChangesW(
        SafeFileHandle hDirectory,
        IntPtr lpBuffer,
        uint nBufferLength,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        uint dwNotifyFilter,
        IntPtr lpBytesReturned,
        IntPtr lpOverlapped,
        IntPtr lpCompletionRoutine);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResult(
        SafeFileHandle hFile,
        IntPtr lpOverlapped,
        out uint lpNumberOfBytesTransferred,
        [MarshalAs(UnmanagedType.Bool)] bool bWait);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(
        SafeFileHandle hFile,
        IntPtr lpOverlapped);
}
