using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lumine.Library;

public sealed class LibraryFilesystemCompatibilityException : IOException
{
    public LibraryFilesystemCompatibilityException(string message)
        : base(message)
    {
    }
}

public static class WindowsFilesystemSemantics
{
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int FileCaseSensitiveInfo = 23;
    private const uint FileCsFlagCaseSensitiveDir = 0x00000001;

    public static bool IsCaseSensitiveDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var handle = CreateFileW(
            Path.GetFullPath(path),
            0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return false;
        }

        if (!GetFileInformationByHandleEx(
                handle,
                FileCaseSensitiveInfo,
                out var info,
                (uint)Marshal.SizeOf<FileCaseSensitiveInformation>()))
        {
            return false;
        }

        return (info.Flags & FileCsFlagCaseSensitiveDir) != 0;
    }

    public static void RequireCaseInsensitiveDirectory(string path)
    {
        if (IsCaseSensitiveDirectory(path))
        {
            throw new LibraryFilesystemCompatibilityException(
                $"Directory uses per-directory case sensitivity, which is not compatible with Lumine's current case-insensitive Library identity contract: {path}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileCaseSensitiveInformation
    {
        public uint Flags;
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
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        out FileCaseSensitiveInformation lpFileInformation,
        uint dwBufferSize);
}
