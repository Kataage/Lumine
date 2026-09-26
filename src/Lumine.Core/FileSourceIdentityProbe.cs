using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lumine.Core;

public sealed record FileSourceIdentity(
    string Value,
    bool UsedFullHash,
    long BytesHashed)
{
    public bool IsFastIdentity =>
        Value.StartsWith("ntfs-usn:", StringComparison.Ordinal);
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

        if (OperatingSystem.IsWindows()
            && TryReadNtfsUsn(stream.SafeFileHandle, out var usn))
        {
            return new FileSourceIdentity(
                $"ntfs-usn:{unchecked((ulong)usn):x16}",
                false,
                0);
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
            FileShare.ReadWrite | FileShare.Delete,
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
            var hex = value.AsSpan("ntfs-usn:".Length);
            return hex.Length == 16
                && hex.ToString().All(static character => Uri.IsHexDigit(character));
        }

        if (value.StartsWith("sha256:", StringComparison.Ordinal))
        {
            var hex = value.AsSpan("sha256:".Length);
            return hex.Length == 64
                && hex.ToString().All(static character => Uri.IsHexDigit(character));
        }

        return false;
    }

    private static bool TryReadNtfsUsn(
        SafeFileHandle handle,
        out long usn)
    {
        usn = 0;

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

            usn = Marshal.ReadInt64(outputBuffer, 24);
            return usn != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(outputBuffer);
            Marshal.FreeHGlobal(inputBuffer);
        }
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
