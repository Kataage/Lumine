namespace Lumine.Library;

public static class LibraryFileTypes
{
    public static readonly IReadOnlyList<string> DefaultExtensions =
    [
        ".jpg", ".jpeg", ".png", ".webp", ".gif",
        ".avif", ".bmp", ".tif", ".tiff"
    ];

    public static bool TryNormalizeExtension(
        string? value,
        out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate =
            value.Trim().ToLowerInvariant();
        if (!candidate.StartsWith(
                '.',
                StringComparison.Ordinal))
        {
            candidate =
                "." + candidate;
        }

        if (candidate.Length is < 2 or > 16)
        {
            return false;
        }

        for (var index = 1;
             index < candidate.Length;
             index++)
        {
            var character =
                candidate[index];
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }
        }

        normalized = candidate;
        return true;
    }

    public static string GetFormat(string path) =>
        Path.GetExtension(path)
            .TrimStart('.')
            .ToLowerInvariant();
}

public sealed class LibraryFileTypePolicy
{
    private HashSet<string> _extensions;

    public LibraryFileTypePolicy(
        IEnumerable<string>? extensions = null)
    {
        _extensions =
            Normalize(
                extensions
                ?? LibraryFileTypes.DefaultExtensions);
    }

    public IReadOnlyList<string> Extensions =>
        Volatile.Read(
                ref _extensions)
            .OrderBy(
                static item => item,
                StringComparer.Ordinal)
            .ToArray();

    public bool IsSupportedPath(string path) =>
        Volatile.Read(
                ref _extensions)
            .Contains(
                Path.GetExtension(path));

    public string GetFormat(string path) =>
        LibraryFileTypes.GetFormat(path);

    public void Update(
        IEnumerable<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(
            extensions);
        Volatile.Write(
            ref _extensions,
            Normalize(extensions));
    }

    public static IReadOnlyList<string> NormalizeExtensions(
        IEnumerable<string> extensions) =>
        Normalize(extensions)
            .OrderBy(
                static item => item,
                StringComparer.Ordinal)
            .ToArray();

    private static HashSet<string> Normalize(
        IEnumerable<string> extensions)
    {
        var normalized =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
        foreach (var extension in extensions)
        {
            if (!LibraryFileTypes.TryNormalizeExtension(
                    extension,
                    out var value))
            {
                continue;
            }

            normalized.Add(value);
        }

        if (normalized.Count == 0)
        {
            throw new ArgumentException(
                "At least one valid image extension is required.",
                nameof(extensions));
        }

        return normalized;
    }
}
