using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lumine.Diagnostics;

public static class FixtureGenerator
{
    private static readonly string[] Extensions = ["jpg", "png", "webp", "avif"];
    private static readonly DateTimeOffset BaseModifiedAt = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static IEnumerable<FixtureAsset> Enumerate(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        for (var index = 0; index < count; index++)
        {
            yield return Create(index);
        }
    }

    public static FixtureAsset Create(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var extension = Extensions[index % Extensions.Length];
        var folderA = index % 128;
        var folderB = (index / 128) % 128;
        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"asset-{index:D8}.{extension}");
        var relativePath = string.Create(
            CultureInfo.InvariantCulture,
            $"library/{folderA:D3}/{folderB:D3}/{fileName}");

        var width = 512 + (index * 37 % 7680);
        var height = 512 + (index * 53 % 4320);
        var fileSize = 16_384L + ((long)index * 2_654_435_761L % 24_000_000L);
        var modified = BaseModifiedAt.AddSeconds(index % (366 * 24 * 60 * 60));

        return new FixtureAsset(
            index + 1L,
            relativePath,
            fileName,
            extension,
            fileSize,
            modified,
            width,
            height);
    }

    public static string ComputeDigest(int count)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var asset in Enumerate(count))
        {
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{asset.Id}|{asset.RelativePath}|{asset.FileSize}|{asset.ModifiedAtUtc:O}|{asset.Width}|{asset.Height}\n");
            hash.AppendData(Encoding.UTF8.GetBytes(line));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static async Task WriteMetadataJsonAsync(
        string outputPath,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        writer.WriteStartArray();

        foreach (var asset in Enumerate(count))
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonSerializer.Serialize(writer, asset, DiagnosticsJsonContext.Default.FixtureAsset);
        }

        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task MaterializeFileTreeAsync(
        string rootPath,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var fullRoot = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(fullRoot);
        string? lastDirectory = null;

        foreach (var asset in Enumerate(count))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var platformRelativePath = asset.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(fullRoot, platformRelativePath);
            var directory = Path.GetDirectoryName(fullPath)!;
            if (!string.Equals(directory, lastDirectory, StringComparison.Ordinal))
            {
                Directory.CreateDirectory(directory);
                lastDirectory = directory;
            }

            await using var stream = new FileStream(
                fullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 1,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
