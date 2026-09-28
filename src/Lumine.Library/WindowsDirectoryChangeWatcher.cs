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
    private readonly object _lifecycleGate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _watchTask;
    private bool _startupComplete;
    private bool _disposed;

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

        cancellationToken.ThrowIfCancellationRequested();

        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_watchTask is not null)
            {
                throw new InvalidOperationException(
                    "Watcher has already been started.");
            }

            _watchTask = Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        RunLoop(publish, _shutdown.Token);
                    }
                    catch (Exception exception)
                    {
                        _ready.TrySetException(exception);
                        throw;
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        // The caller token owns startup only. Once readiness is observed,
        // the returned watcher/session lifetime is owned exclusively by
        // DisposeAsync rather than by a token the caller may later reuse.
        using var startupCancellation = cancellationToken.Register(
            static state =>
                ((WindowsDirectoryChangeWatcher)state!).CancelStartup(),
            this);

        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task Completion
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _watchTask ?? Task.CompletedTask;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? watchTask;
        var ownsShutdown = false;

        lock (_lifecycleGate)
        {
            if (!_disposed)
            {
                _disposed = true;
                ownsShutdown = true;
            }

            watchTask = _watchTask;
        }

        if (!ownsShutdown)
        {
            await _disposeCompletion.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            _shutdown.Cancel();

            if (watchTask is not null)
            {
                try
                {
                    await watchTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            _shutdown.Dispose();
            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _shutdown.Dispose();
            _disposeCompletion.TrySetException(exception);
            throw;
        }
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

    internal static List<DirectoryChange> ParseBuffer(
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

    private void CancelStartup()
    {
        lock (_lifecycleGate)
        {
            if (_startupComplete || _disposed)
            {
                return;
            }

            _shutdown.Cancel();
        }
    }

    private void MarkStartupReady()
    {
        lock (_lifecycleGate)
        {
            _startupComplete = true;
            _ready.TrySetResult();
        }
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
                // subtree read has actually been armed. Publish readiness
                // under the lifecycle lock so a caller-token cancellation
                // cannot race across the startup/session ownership boundary.
                MarkStartupReady();

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
