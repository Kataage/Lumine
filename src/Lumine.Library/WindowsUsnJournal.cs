using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Lumine.Library;

public sealed record UsnJournalSnapshot(
    bool Available,
    string? JournalId,
    long FirstUsn,
    long LowestValidUsn,
    long NextUsn,
    string? UnavailableReason);

public sealed record UsnCatchUpResult(
    bool AppliedAsDelta,
    bool RequiresReconcile,
    string? Reason,
    string? JournalId,
    long StartUsn,
    long NextUsn,
    long RecordsScanned,
    IReadOnlyList<DirectoryChange> Changes);

public sealed class WindowsUsnJournal
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    private const uint FileDeviceFileSystem = 0x00000009;
    private const uint MethodBuffered = 0;
    private const uint MethodNeither = 3;
    private const uint FileAnyAccess = 0;
    private const uint FsctlReadUsnJournal =
        (FileDeviceFileSystem << 16)
        | (FileAnyAccess << 14)
        | (46u << 2)
        | MethodNeither;
    private const uint FsctlQueryUsnJournal =
        (FileDeviceFileSystem << 16)
        | (FileAnyAccess << 14)
        | (61u << 2)
        | MethodBuffered;

    private const uint FileAttributeDirectory = 0x00000010;

    private const uint UsnReasonDataOverwrite = 0x00000001;
    private const uint UsnReasonDataExtend = 0x00000002;
    private const uint UsnReasonDataTruncation = 0x00000004;
    private const uint UsnReasonNamedDataOverwrite = 0x00000010;
    private const uint UsnReasonNamedDataExtend = 0x00000020;
    private const uint UsnReasonNamedDataTruncation = 0x00000040;
    private const uint UsnReasonFileCreate = 0x00000100;
    private const uint UsnReasonFileDelete = 0x00000200;
    private const uint UsnReasonEaChange = 0x00000400;
    private const uint UsnReasonSecurityChange = 0x00000800;
    private const uint UsnReasonRenameOldName = 0x00001000;
    private const uint UsnReasonRenameNewName = 0x00002000;
    private const uint UsnReasonIndexableChange = 0x00004000;
    private const uint UsnReasonBasicInfoChange = 0x00008000;
    private const uint UsnReasonHardLinkChange = 0x00010000;
    private const uint UsnReasonCompressionChange = 0x00020000;
    private const uint UsnReasonEncryptionChange = 0x00040000;
    private const uint UsnReasonObjectIdChange = 0x00080000;
    private const uint UsnReasonReparsePointChange = 0x00100000;
    private const uint UsnReasonStreamChange = 0x00200000;

    private const uint RelevantReasonMask =
        UsnReasonDataOverwrite
        | UsnReasonDataExtend
        | UsnReasonDataTruncation
        | UsnReasonNamedDataOverwrite
        | UsnReasonNamedDataExtend
        | UsnReasonNamedDataTruncation
        | UsnReasonFileCreate
        | UsnReasonFileDelete
        | UsnReasonEaChange
        | UsnReasonSecurityChange
        | UsnReasonRenameOldName
        | UsnReasonRenameNewName
        | UsnReasonIndexableChange
        | UsnReasonBasicInfoChange
        | UsnReasonHardLinkChange
        | UsnReasonCompressionChange
        | UsnReasonEncryptionChange
        | UsnReasonObjectIdChange
        | UsnReasonReparsePointChange
        | UsnReasonStreamChange;

    private const int OutputBufferSize = 64 * 1024;
    private const int MaxRelevantChanges = 4096;
    private const long MaxRecordsScanned = 100_000;

    public static UsnJournalSnapshot Query(string libraryRoot)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new UsnJournalSnapshot(
                false,
                null,
                0,
                0,
                0,
                "USN Journal is Windows-only.");
        }

        if (!TryGetVolume(libraryRoot, out var volumeRoot, out var volumePath, out var reason))
        {
            return new UsnJournalSnapshot(
                false,
                null,
                0,
                0,
                0,
                reason);
        }

        var fileSystem = new char[32];
        if (!GetVolumeInformationW(
                volumeRoot,
                null,
                0,
                out _,
                out _,
                out _,
                fileSystem,
                fileSystem.Length))
        {
            return new UsnJournalSnapshot(
                false,
                null,
                0,
                0,
                0,
                $"GetVolumeInformationW failed: {Marshal.GetLastWin32Error()}.");
        }

        if (!string.Equals(
                ReadNullTerminated(fileSystem),
                "NTFS",
                StringComparison.OrdinalIgnoreCase))
        {
            return new UsnJournalSnapshot(
                false,
                null,
                0,
                0,
                0,
                $"Filesystem '{ReadNullTerminated(fileSystem)}' is not NTFS.");
        }

        using var handle = CreateFileW(
            volumePath,
            GenericRead,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            return new UsnJournalSnapshot(
                false,
                null,
                0,
                0,
                0,
                $"Opening NTFS volume for USN Journal failed: {error}.");
        }

        var size = Marshal.SizeOf<UsnJournalDataV0>();
        var output = Marshal.AllocHGlobal(size);

        try
        {
            if (!DeviceIoControl(
                    handle,
                    FsctlQueryUsnJournal,
                    IntPtr.Zero,
                    0,
                    output,
                    (uint)size,
                    out _,
                    IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                return new UsnJournalSnapshot(
                    false,
                    null,
                    0,
                    0,
                    0,
                    $"FSCTL_QUERY_USN_JOURNAL failed: {error}.");
            }

            var data = Marshal.PtrToStructure<UsnJournalDataV0>(output);
            return new UsnJournalSnapshot(
                true,
                data.UsnJournalId.ToString("X16", CultureInfo.InvariantCulture),
                data.FirstUsn,
                data.LowestValidUsn,
                data.NextUsn,
                null);
        }
        finally
        {
            Marshal.FreeHGlobal(output);
        }
    }

    public static UsnCatchUpResult ReadChanges(
        string libraryRoot,
        string expectedJournalId,
        long startUsn,
        long endUsn,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedJournalId);

        var snapshot = Query(libraryRoot);
        if (!snapshot.Available || snapshot.JournalId is null)
        {
            return RequiresReconcile(
                expectedJournalId,
                startUsn,
                startUsn,
                snapshot.UnavailableReason ?? "USN Journal is unavailable.");
        }

        if (!string.Equals(
                snapshot.JournalId,
                expectedJournalId,
                StringComparison.OrdinalIgnoreCase))
        {
            return RequiresReconcile(
                snapshot.JournalId,
                startUsn,
                snapshot.NextUsn,
                "USN journal identifier changed.");
        }

        var oldestValid = Math.Max(
            snapshot.FirstUsn,
            snapshot.LowestValidUsn);

        if (startUsn < oldestValid || startUsn > snapshot.NextUsn)
        {
            return RequiresReconcile(
                snapshot.JournalId,
                startUsn,
                snapshot.NextUsn,
                $"USN checkpoint {startUsn} is outside valid range {oldestValid}..{snapshot.NextUsn}.");
        }

        var targetUsn = Math.Clamp(
            endUsn,
            startUsn,
            snapshot.NextUsn);

        if (targetUsn == startUsn)
        {
            return new UsnCatchUpResult(
                true,
                false,
                null,
                snapshot.JournalId,
                startUsn,
                targetUsn,
                0,
                []);
        }

        if (!TryGetVolume(libraryRoot, out _, out var volumePath, out var volumeReason))
        {
            return RequiresReconcile(
                snapshot.JournalId,
                startUsn,
                targetUsn,
                volumeReason ?? "Volume path could not be resolved.");
        }

        using var volume = CreateFileW(
            volumePath,
            GenericRead,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (volume.IsInvalid)
        {
            return RequiresReconcile(
                snapshot.JournalId,
                startUsn,
                targetUsn,
                $"Opening volume for USN read failed: {Marshal.GetLastWin32Error()}.");
        }

        if (!ulong.TryParse(
                snapshot.JournalId,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var journalId))
        {
            return RequiresReconcile(
                snapshot.JournalId,
                startUsn,
                targetUsn,
                "USN journal identifier could not be parsed.");
        }

        var changes = new List<DirectoryChange>();
        var pendingRenameOld = new Dictionary<ulong, string>();
        long recordsScanned = 0;
        var currentUsn = startUsn;

        var inputSize = Marshal.SizeOf<ReadUsnJournalDataV0>();
        var input = Marshal.AllocHGlobal(inputSize);
        var output = Marshal.AllocHGlobal(OutputBufferSize);

        try
        {
            while (currentUsn < targetUsn)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var read = new ReadUsnJournalDataV0
                {
                    StartUsn = currentUsn,
                    ReasonMask = RelevantReasonMask,
                    ReturnOnlyOnClose = 0,
                    Timeout = 0,
                    BytesToWaitFor = 0,
                    UsnJournalId = journalId
                };

                Marshal.StructureToPtr(read, input, false);

                if (!DeviceIoControl(
                        volume,
                        FsctlReadUsnJournal,
                        input,
                        (uint)inputSize,
                        output,
                        OutputBufferSize,
                        out var bytesReturned,
                        IntPtr.Zero))
                {
                    return RequiresReconcile(
                        snapshot.JournalId,
                        startUsn,
                        targetUsn,
                        $"FSCTL_READ_USN_JOURNAL failed: {Marshal.GetLastWin32Error()}.");
                }

                if (bytesReturned < sizeof(long))
                {
                    return RequiresReconcile(
                        snapshot.JournalId,
                        startUsn,
                        targetUsn,
                        "USN read returned a malformed buffer.");
                }

                var nextUsn = Marshal.ReadInt64(output);
                var offset = sizeof(long);

                while ((uint)offset < bytesReturned)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var record = IntPtr.Add(output, offset);
                    var recordLength = Marshal.ReadInt32(record, 0);
                    var majorVersion = (ushort)Marshal.ReadInt16(record, 4);

                    if (recordLength < 60
                        || (uint)(offset + recordLength) > bytesReturned
                        || majorVersion != 2)
                    {
                        return RequiresReconcile(
                            snapshot.JournalId,
                            startUsn,
                            targetUsn,
                            $"Unsupported or malformed USN record version {majorVersion}.");
                    }

                    var recordUsn = Marshal.ReadInt64(record, 24);
                    if (recordUsn >= targetUsn)
                    {
                        offset += recordLength;
                        continue;
                    }

                    recordsScanned++;
                    if (recordsScanned > MaxRecordsScanned)
                    {
                        return RequiresReconcile(
                            snapshot.JournalId,
                            startUsn,
                            targetUsn,
                            $"USN catch-up exceeded {MaxRecordsScanned:N0} records.");
                    }

                    var fileReference = unchecked((ulong)Marshal.ReadInt64(record, 8));
                    var parentReference = unchecked((ulong)Marshal.ReadInt64(record, 16));
                    var reasonFlags = unchecked((uint)Marshal.ReadInt32(record, 40));
                    var attributes = unchecked((uint)Marshal.ReadInt32(record, 52));
                    var nameLength = (ushort)Marshal.ReadInt16(record, 56);
                    var nameOffset = (ushort)Marshal.ReadInt16(record, 58);

                    if (nameLength == 0
                        || nameLength % 2 != 0
                        || nameOffset < 60
                        || nameOffset + nameLength > recordLength)
                    {
                        return RequiresReconcile(
                            snapshot.JournalId,
                            startUsn,
                            targetUsn,
                            "USN record contained an invalid filename.");
                    }

                    var fileName = Marshal.PtrToStringUni(
                        IntPtr.Add(record, nameOffset),
                        nameLength / 2);

                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        offset += recordLength;
                        continue;
                    }

                    var preferRecordedPath =
                        (reasonFlags & (UsnReasonRenameOldName | UsnReasonFileDelete)) != 0;

                    var resolution = ResolveRelativePath(
                        volume,
                        libraryRoot,
                        fileReference,
                        parentReference,
                        fileName,
                        preferRecordedPath);

                    if (resolution.Kind == PathResolutionKind.Unresolved)
                    {
                        return RequiresReconcile(
                            snapshot.JournalId,
                            startUsn,
                            targetUsn,
                            $"USN path could not be resolved for record {fileReference:X16}.");
                    }

                    if (resolution.Kind == PathResolutionKind.OutsideLibrary)
                    {
                        offset += recordLength;
                        continue;
                    }

                    var relativePath = resolution.RelativePath!;

                    if ((attributes & FileAttributeDirectory) != 0)
                    {
                        return RequiresReconcile(
                            snapshot.JournalId,
                            startUsn,
                            targetUsn,
                            $"Directory journal change requires reconciliation: {relativePath}.");
                    }

                    if (!LibraryFileTypes.IsSupportedPath(relativePath))
                    {
                        offset += recordLength;
                        continue;
                    }

                    var observedAt = DateTimeOffset.UtcNow;

                    if ((reasonFlags & UsnReasonRenameOldName) != 0)
                    {
                        pendingRenameOld[fileReference] = relativePath;
                    }

                    if ((reasonFlags & UsnReasonRenameNewName) != 0)
                    {
                        if (pendingRenameOld.Remove(fileReference, out var oldPath))
                        {
                            changes.Add(
                                new DirectoryChange(
                                    DirectoryChangeKind.Renamed,
                                    relativePath,
                                    oldPath,
                                    observedAt));
                        }
                        else
                        {
                            changes.Add(
                                new DirectoryChange(
                                    DirectoryChangeKind.Added,
                                    relativePath,
                                    null,
                                    observedAt));
                        }
                    }
                    else if ((reasonFlags & UsnReasonFileDelete) != 0)
                    {
                        changes.Add(
                            new DirectoryChange(
                                DirectoryChangeKind.Removed,
                                relativePath,
                                null,
                                observedAt));
                    }
                    else if ((reasonFlags & UsnReasonFileCreate) != 0)
                    {
                        changes.Add(
                            new DirectoryChange(
                                DirectoryChangeKind.Added,
                                relativePath,
                                null,
                                observedAt));
                    }
                    else if ((reasonFlags & RelevantReasonMask) != 0
                             && (reasonFlags & UsnReasonRenameOldName) == 0)
                    {
                        changes.Add(
                            new DirectoryChange(
                                DirectoryChangeKind.Modified,
                                relativePath,
                                null,
                                observedAt));
                    }

                    if (changes.Count > MaxRelevantChanges)
                    {
                        return RequiresReconcile(
                            snapshot.JournalId,
                            startUsn,
                            targetUsn,
                            $"USN catch-up exceeded {MaxRelevantChanges:N0} relevant changes.");
                    }

                    offset += recordLength;
                }

                if (nextUsn <= currentUsn)
                {
                    break;
                }

                currentUsn = Math.Min(nextUsn, targetUsn);
            }

            if (pendingRenameOld.Count > 0)
            {
                foreach (var oldPath in pendingRenameOld.Values)
                {
                    changes.Add(
                        new DirectoryChange(
                            DirectoryChangeKind.Removed,
                            oldPath,
                            null,
                            DateTimeOffset.UtcNow));
                }
            }

            return new UsnCatchUpResult(
                true,
                false,
                null,
                snapshot.JournalId,
                startUsn,
                targetUsn,
                recordsScanned,
                changes);
        }
        finally
        {
            Marshal.FreeHGlobal(output);
            Marshal.FreeHGlobal(input);
        }
    }

    private static UsnCatchUpResult RequiresReconcile(
        string? journalId,
        long startUsn,
        long nextUsn,
        string reason) =>
        new(
            false,
            true,
            reason,
            journalId,
            startUsn,
            nextUsn,
            0,
            []);

    private static PathResolution ResolveRelativePath(
        SafeFileHandle volume,
        string libraryRoot,
        ulong fileReference,
        ulong parentReference,
        string fileName,
        bool preferRecordedPath)
    {
        if (preferRecordedPath)
        {
            var recordedParent = TryOpenPathById(volume, parentReference);
            if (recordedParent is not null)
            {
                return ResolveKnownFullPath(
                    libraryRoot,
                    Path.Combine(recordedParent, fileName));
            }

            return new PathResolution(
                PathResolutionKind.Unresolved,
                null);
        }

        var current = TryOpenPathById(volume, fileReference);
        if (current is not null)
        {
            return ResolveKnownFullPath(libraryRoot, current);
        }

        var parent = TryOpenPathById(volume, parentReference);
        if (parent is null)
        {
            return new PathResolution(
                PathResolutionKind.Unresolved,
                null);
        }

        return ResolveKnownFullPath(
            libraryRoot,
            Path.Combine(parent, fileName));
    }

    private static PathResolution ResolveKnownFullPath(
        string libraryRoot,
        string fullPath) =>
        TryMakeRelative(libraryRoot, fullPath, out var relative)
            ? new PathResolution(PathResolutionKind.InsideLibrary, relative)
            : new PathResolution(PathResolutionKind.OutsideLibrary, null);

    private static string? TryOpenPathById(
        SafeFileHandle volume,
        ulong fileReference)
    {
        var descriptor = new FileIdDescriptor
        {
            Size = (uint)Marshal.SizeOf<FileIdDescriptor>(),
            Type = 0,
            FileId = unchecked((long)fileReference)
        };

        using var handle = OpenFileById(
            volume,
            ref descriptor,
            0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            FileFlagBackupSemantics);

        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new char[512];
        var length = GetFinalPathNameByHandleW(
            handle,
            buffer,
            (uint)buffer.Length,
            0);

        if (length == 0)
        {
            return null;
        }

        if (length >= buffer.Length)
        {
            buffer = new char[checked((int)length + 1)];
            length = GetFinalPathNameByHandleW(
                handle,
                buffer,
                (uint)buffer.Length,
                0);

            if (length == 0 || length >= buffer.Length)
            {
                return null;
            }
        }

        return NormalizeFinalPath(
            new string(buffer, 0, checked((int)length)));
    }

    private static string ReadNullTerminated(char[] buffer)
    {
        var length = Array.IndexOf(buffer, '\0');
        if (length < 0)
        {
            length = buffer.Length;
        }

        return new string(buffer, 0, length);
    }

    private static string NormalizeFinalPath(string path)
    {
        const string dosPrefix = @"\\?\";
        const string uncPrefix = @"\\?\UNC\";

        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[uncPrefix.Length..];
        }

        return path.StartsWith(dosPrefix, StringComparison.OrdinalIgnoreCase)
            ? path[dosPrefix.Length..]
            : path;
    }

    private static bool TryMakeRelative(
        string libraryRoot,
        string fullPath,
        out string relativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(libraryRoot));
        var candidate = Path.GetFullPath(fullPath);

        if (!candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            relativePath = string.Empty;
            return false;
        }

        var relative = Path.GetRelativePath(root, candidate);
        if (relative is "." or "")
        {
            relativePath = string.Empty;
            return false;
        }

        relativePath = LibraryPaths.NormalizeRelativePath(relative);
        return true;
    }

    private static bool TryGetVolume(
        string libraryRoot,
        out string volumeRoot,
        out string volumePath,
        out string? reason)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(libraryRoot));

        if (string.IsNullOrWhiteSpace(root)
            || root.Length < 2
            || root[1] != ':')
        {
            volumeRoot = string.Empty;
            volumePath = string.Empty;
            reason = "USN catch-up currently requires a local drive-letter NTFS volume.";
            return false;
        }

        volumeRoot = root;
        volumePath = $@"\\.\{char.ToUpperInvariant(root[0])}:";
        reason = null;
        return true;
    }

    private enum PathResolutionKind
    {
        InsideLibrary = 1,
        OutsideLibrary = 2,
        Unresolved = 3
    }

    private readonly record struct PathResolution(
        PathResolutionKind Kind,
        string? RelativePath);

    [StructLayout(LayoutKind.Sequential)]
    private struct UsnJournalDataV0
    {
        public ulong UsnJournalId;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReadUsnJournalDataV0
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct FileIdDescriptor
    {
        [FieldOffset(0)]
        public uint Size;

        [FieldOffset(4)]
        public uint Type;

        [FieldOffset(8)]
        public long FileId;
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
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    private static extern SafeFileHandle OpenFileById(
        SafeFileHandle hVolumeHint,
        ref FileIdDescriptor lpFileId,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwFlagsAndAttributes);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile,
        [Out] char[] lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string lpRootPathName,
        [Out] char[]? lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        [Out] char[] lpFileSystemNameBuffer,
        int nFileSystemNameSize);
}
