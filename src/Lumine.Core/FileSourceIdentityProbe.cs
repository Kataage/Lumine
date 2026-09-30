using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lumine.Core;

public enum FileSourceIdentityKind
{
    NtfsUsn = 0,
    WindowsFileId = 1,
    Sha256 = 2
}

public sealed record FileSourceIdentity(
    string Value,
    bool UsedFullHash,
    long BytesHashed)
{
    public FileSourceIdentityKind Kind =>
        FileSourceIdentityProbe.GetKind(Value);

    public bool IsFastIdentity =>
        Kind is not FileSourceIdentityKind.Sha256;
}

public static class FileSourceIdentityProbe
{
    private const string NtfsUsnPrefix = "ntfs-usn:";
    private const string WindowsFileIdPrefix = "win-fileid-v1:";
    private const string Sha256Prefix = "sha256:";

    private const uint FileDeviceFileSystem = 0x00000009;
    private const uint MethodNeither = 3;
    private const uint FileAnyAccess = 0;
    private const uint FsctlReadFileUsnData =
        (FileDeviceFileSystem << 16)
        | (FileAnyAccess << 14)
        | (58u << 2)
        | MethodNeither;

    private const int FileBasicInfoClass = 0;
    private const int FileIdInfoClass = 18;

    public static FileSourceIdentity Read(
        FileStream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        if (OperatingSystem.IsWindows())
        {
            if (TryReadNtfsIdentity(
                    stream.SafeFileHandle,
                    out var usn,
                    out var changeTime))
            {
                return new FileSourceIdentity(
                    $"{NtfsUsnPrefix}{unchecked((ulong)usn):x16}:{unchecked((ulong)changeTime):x16}",
                    false,
                    0);
            }

            var windowsFileId = ReadWindowsFileIdIdentity(stream);
            if (windowsFileId is not null)
            {
                return windowsFileId;
            }
        }

        return ReadFullHash(stream, cancellationToken);
    }

    public static FileSourceIdentity Read(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);

        return Read(stream, cancellationToken);
    }

    public static FileSourceIdentityKind GetKind(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.StartsWith(
                NtfsUsnPrefix,
                StringComparison.Ordinal))
        {
            return FileSourceIdentityKind.NtfsUsn;
        }

        if (value.StartsWith(
                WindowsFileIdPrefix,
                StringComparison.Ordinal))
        {
            return FileSourceIdentityKind.WindowsFileId;
        }

        if (value.StartsWith(
                Sha256Prefix,
                StringComparison.Ordinal))
        {
            return FileSourceIdentityKind.Sha256;
        }

        throw new ArgumentException(
            "Unknown source identity scheme.",
            nameof(value));
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith(
                NtfsUsnPrefix,
                StringComparison.Ordinal))
        {
            var payload = value.AsSpan(NtfsUsnPrefix.Length);
            return payload.Length == 33
                && payload[16] == ':'
                && IsHex(payload[..16], 16)
                && IsHex(payload[17..], 16);
        }

        if (value.StartsWith(
                WindowsFileIdPrefix,
                StringComparison.Ordinal))
        {
            // volume serial (16)
            // : 128-bit file id (32)
            // : file size (16)
            // : last-write FILETIME (16)
            // : change FILETIME (16)
            var payload =
                value.AsSpan(WindowsFileIdPrefix.Length);

            return payload.Length == 100
                && payload[16] == ':'
                && payload[49] == ':'
                && payload[66] == ':'
                && payload[83] == ':'
                && IsHex(payload[..16], 16)
                && IsHex(payload.Slice(17, 32), 32)
                && IsHex(payload.Slice(50, 16), 16)
                && IsHex(payload.Slice(67, 16), 16)
                && IsHex(payload.Slice(84, 16), 16);
        }

        if (value.StartsWith(
                Sha256Prefix,
                StringComparison.Ordinal))
        {
            return IsHex(
                value.AsSpan(Sha256Prefix.Length),
                64);
        }

        return false;
    }

    public static string Normalize(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                "Source identity must be a valid NTFS-USN, Windows file-id, or SHA-256 identity.",
                nameof(value));
        }

        return value.ToLowerInvariant();
    }

    internal static FileSourceIdentity? ReadWindowsFileIdIdentity(
        FileStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!TryReadWindowsFileIdIdentity(
                stream.SafeFileHandle,
                stream.Length,
                out var volumeSerial,
                out var fileIdLow,
                out var fileIdHigh,
                out var lastWriteTime,
                out var changeTime))
        {
            return null;
        }

        return new FileSourceIdentity(
            $"{WindowsFileIdPrefix}{volumeSerial:x16}:{fileIdLow:x16}{fileIdHigh:x16}:{unchecked((ulong)stream.Length):x16}:{unchecked((ulong)lastWriteTime):x16}:{unchecked((ulong)changeTime):x16}",
            false,
            0);
    }

    private static FileSourceIdentity ReadFullHash(
        FileStream stream,
        CancellationToken cancellationToken)
    {
        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            using var incremental =
                System.Security.Cryptography.IncrementalHash.CreateHash(
                    System.Security.Cryptography.HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long bytesHashed = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(
                    buffer,
                    0,
                    buffer.Length);
                if (read == 0)
                {
                    break;
                }

                incremental.AppendData(
                    buffer,
                    0,
                    read);
                bytesHashed += read;
            }

            var digest = Convert.ToHexString(
                    incremental.GetHashAndReset())
                .ToLowerInvariant();

            return new FileSourceIdentity(
                $"{Sha256Prefix}{digest}",
                true,
                bytesHashed);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static bool IsHex(
        ReadOnlySpan<char> value,
        int expectedLength)
    {
        if (value.Length != expectedLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            var isDigit =
                character is >= '0' and <= '9';
            var lower =
                character | (char)0x20;
            var isHexLetter =
                lower is >= 'a' and <= 'f';

            if (!isDigit && !isHexLetter)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadNtfsIdentity(
        SafeFileHandle handle,
        out long usn,
        out long changeTime)
    {
        usn = 0;
        changeTime = 0;

        var input = new ReadFileUsnData
        {
            MinMajorVersion = 2,
            MaxMajorVersion = 2
        };

        var inputSize =
            Marshal.SizeOf<ReadFileUsnData>();
        var inputBuffer =
            Marshal.AllocHGlobal(inputSize);
        var outputBuffer =
            Marshal.AllocHGlobal(512);

        try
        {
            Marshal.StructureToPtr(
                input,
                inputBuffer,
                false);

            if (!DeviceIoControl(
                    handle,
                    FsctlReadFileUsnData,
                    inputBuffer,
                    (uint)inputSize,
                    outputBuffer,
                    512,
                    out var bytesReturned,
                    IntPtr.Zero))
            {
                return false;
            }

            if (bytesReturned < 60)
            {
                return false;
            }

            var recordLength =
                Marshal.ReadInt32(
                    outputBuffer,
                    0);
            var majorVersion =
                (ushort)Marshal.ReadInt16(
                    outputBuffer,
                    4);

            if (recordLength < 60
                || recordLength > bytesReturned
                || majorVersion != 2)
            {
                return false;
            }

            if (!TryReadBasicInfo(
                    handle,
                    out var basicInfo))
            {
                return false;
            }

            usn = Marshal.ReadInt64(
                outputBuffer,
                24);
            changeTime =
                basicInfo.ChangeTime;
            return usn != 0
                && changeTime != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(outputBuffer);
            Marshal.FreeHGlobal(inputBuffer);
        }
    }

    private static bool TryReadWindowsFileIdIdentity(
        SafeFileHandle handle,
        long fileSize,
        out ulong volumeSerial,
        out ulong fileIdLow,
        out ulong fileIdHigh,
        out long lastWriteTime,
        out long changeTime)
    {
        volumeSerial = 0;
        fileIdLow = 0;
        fileIdHigh = 0;
        lastWriteTime = 0;
        changeTime = 0;

        if (fileSize < 0
            || !TryReadBasicInfo(
                handle,
                out var basicInfo))
        {
            return false;
        }

        var fileIdInfoSize =
            Marshal.SizeOf<FileIdInfo>();
        var fileIdInfoBuffer =
            Marshal.AllocHGlobal(fileIdInfoSize);

        try
        {
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileIdInfoClass,
                    fileIdInfoBuffer,
                    (uint)fileIdInfoSize))
            {
                return false;
            }

            var fileIdInfo =
                Marshal.PtrToStructure<FileIdInfo>(
                    fileIdInfoBuffer);

            if (fileIdInfo.VolumeSerialNumber == 0
                || (fileIdInfo.FileIdLow == 0
                    && fileIdInfo.FileIdHigh == 0)
                || basicInfo.LastWriteTime == 0
                || basicInfo.ChangeTime == 0)
            {
                return false;
            }

            volumeSerial =
                fileIdInfo.VolumeSerialNumber;
            fileIdLow =
                fileIdInfo.FileIdLow;
            fileIdHigh =
                fileIdInfo.FileIdHigh;
            lastWriteTime =
                basicInfo.LastWriteTime;
            changeTime =
                basicInfo.ChangeTime;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(fileIdInfoBuffer);
        }
    }

    private static bool TryReadBasicInfo(
        SafeFileHandle handle,
        out FileBasicInfo basicInfo)
    {
        basicInfo = default;

        var basicInfoSize =
            Marshal.SizeOf<FileBasicInfo>();
        var basicInfoBuffer =
            Marshal.AllocHGlobal(basicInfoSize);

        try
        {
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileBasicInfoClass,
                    basicInfoBuffer,
                    (uint)basicInfoSize))
            {
                return false;
            }

            basicInfo =
                Marshal.PtrToStructure<FileBasicInfo>(
                    basicInfoBuffer);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(basicInfoBuffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInfo
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReadFileUsnData
    {
        public ushort MinMajorVersion;
        public ushort MaxMajorVersion;
    }

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        IntPtr lpFileInformation,
        uint dwBufferSize);

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
}
