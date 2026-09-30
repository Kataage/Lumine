using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lumine.Core;

public sealed record FileSourceIdentity(
    string Value,
    bool UsedFullHash,
    long BytesHashed)
{
    public bool IsFastIdentity =>
        Value.StartsWith("ntfs-usn:", StringComparison.Ordinal)
        || Value.StartsWith("win-fileid:", StringComparison.Ordinal);
}

public static class FileSourceIdentityProbe
{
    private const uint FileDeviceFileSystem = 0x00000009;
    private const uint MethodNeither = 3;
    private const uint FileAnyAccess = 0;
    private const uint FsctlReadFileUsnData =
        (FileDeviceFileSystem << 16)
        | (FileAnyAccess << 14)
        | (58u << 2)
        | MethodNeither;

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
                    $"ntfs-usn:{unchecked((ulong)usn):x16}:{unchecked((ulong)changeTime):x16}",
                    false,
                    0);
            }

            if (TryReadWindowsFileIdIdentity(
                    stream,
                    out var windowsIdentity))
            {
                return windowsIdentity;
            }
        }

        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            using var incremental = System.Security.Cryptography.IncrementalHash.CreateHash(
                System.Security.Cryptography.HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long bytesHashed = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                incremental.AppendData(buffer, 0, read);
                bytesHashed += read;
            }

            var digest = Convert.ToHexString(
                    incremental.GetHashAndReset())
                .ToLowerInvariant();

            return new FileSourceIdentity(
                $"sha256:{digest}",
                true,
                bytesHashed);
        }
        finally
        {
            stream.Position = originalPosition;
        }
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

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("ntfs-usn:", StringComparison.Ordinal))
        {
            var payload = value.AsSpan("ntfs-usn:".Length);
            return payload.Length == 33
                && payload[16] == ':'
                && IsHex(payload[..16], 16)
                && IsHex(payload[17..], 16);
        }

        if (value.StartsWith("win-fileid:", StringComparison.Ordinal))
        {
            var payload = value.AsSpan("win-fileid:".Length);
            return payload.Length == 117
                && payload[16] == ':'
                && payload[49] == ':'
                && payload[66] == ':'
                && payload[83] == ':'
                && payload[100] == ':'
                && IsHex(payload[..16], 16)
                && IsHex(payload.Slice(17, 32), 32)
                && IsHex(payload.Slice(50, 16), 16)
                && IsHex(payload.Slice(67, 16), 16)
                && IsHex(payload.Slice(84, 16), 16)
                && IsHex(payload.Slice(101, 16), 16);
        }

        if (value.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return IsHex(
                value.AsSpan("sha256:".Length),
                64);
        }

        return false;
    }

    public static string Normalize(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                "Source identity must be a valid NTFS-USN, Windows file-ID, or SHA-256 identity.",
                nameof(value));
        }

        return value.ToLowerInvariant();
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
            var isDigit = character is >= '0' and <= '9';
            var lower = character | (char)0x20;
            var isHexLetter = lower is >= 'a' and <= 'f';

            if (!isDigit && !isHexLetter)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TryReadWindowsFileIdIdentity(
        FileStream stream,
        out FileSourceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(stream);
        identity = default!;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var fileIdBuffer = Marshal.AllocHGlobal(FileIdInfoSize);
        var basicInfoSize = Marshal.SizeOf<FileBasicInfo>();
        var basicInfoBuffer = Marshal.AllocHGlobal(basicInfoSize);

        try
        {
            if (!GetFileInformationByHandleEx(
                    stream.SafeFileHandle,
                    FileIdInfoClass,
                    fileIdBuffer,
                    (uint)FileIdInfoSize)
                || !GetFileInformationByHandleEx(
                    stream.SafeFileHandle,
                    FileBasicInfoClass,
                    basicInfoBuffer,
                    (uint)basicInfoSize))
            {
                return false;
            }

            var volumeSerial =
                unchecked((ulong)Marshal.ReadInt64(fileIdBuffer, 0));
            var fileIdLow =
                unchecked((ulong)Marshal.ReadInt64(fileIdBuffer, 8));
            var fileIdHigh =
                unchecked((ulong)Marshal.ReadInt64(fileIdBuffer, 16));
            var basicInfo =
                Marshal.PtrToStructure<FileBasicInfo>(
                    basicInfoBuffer);
            var length = stream.Length;

            if ((fileIdLow | fileIdHigh) == 0
                || basicInfo.LastWriteTime == 0
                || basicInfo.ChangeTime == 0
                || length < 0)
            {
                return false;
            }

            identity = new FileSourceIdentity(
                $"win-fileid:{volumeSerial:x16}:{fileIdLow:x16}{fileIdHigh:x16}:{unchecked((ulong)basicInfo.CreationTime):x16}:{unchecked((ulong)basicInfo.LastWriteTime):x16}:{unchecked((ulong)basicInfo.ChangeTime):x16}:{unchecked((ulong)length):x16}",
                false,
                0);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(basicInfoBuffer);
            Marshal.FreeHGlobal(fileIdBuffer);
        }
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

        var inputSize = Marshal.SizeOf<ReadFileUsnData>();
        var inputBuffer = Marshal.AllocHGlobal(inputSize);
        var outputBuffer = Marshal.AllocHGlobal(512);

        try
        {
            Marshal.StructureToPtr(input, inputBuffer, false);

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

            var recordLength = Marshal.ReadInt32(outputBuffer, 0);
            var majorVersion = (ushort)Marshal.ReadInt16(outputBuffer, 4);

            if (recordLength < 60
                || recordLength > bytesReturned
                || majorVersion != 2)
            {
                return false;
            }

            var basicInfoSize = Marshal.SizeOf<FileBasicInfo>();
            var basicInfoBuffer = Marshal.AllocHGlobal(basicInfoSize);

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

                var basicInfo =
                    Marshal.PtrToStructure<FileBasicInfo>(
                        basicInfoBuffer);

                usn = Marshal.ReadInt64(outputBuffer, 24);
                changeTime = basicInfo.ChangeTime;
                return usn != 0 && changeTime != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(basicInfoBuffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(outputBuffer);
            Marshal.FreeHGlobal(inputBuffer);
        }
    }

    private const int FileBasicInfoClass = 0;
    private const int FileIdInfoClass = 18;
    private const int FileIdInfoSize = 24;

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
