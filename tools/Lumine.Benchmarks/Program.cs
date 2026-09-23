using System.Globalization;
using Lumine.Diagnostics;

static string? ReadOption(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.Ordinal))
        {
            return args[index + 1];
        }
    }

    return null;
}

var countText = ReadOption(args, "--count") ?? "100000";
if (!int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
{
    throw new ArgumentException("--count must be a positive integer.");
}

var output = ReadOption(args, "--output") ?? Path.Combine("artifacts", "benchmarks", $"core-{count}.json");
var metadataOutput = ReadOption(args, "--metadata-output");
var treeRoot = ReadOption(args, "--tree-root");

var recorder = new BenchmarkRecorder();
string digest;

using (recorder.Measure(CoreMetricNames.FixtureMetadataGeneration))
{
    digest = FixtureGenerator.ComputeDigest(count);
}

if (!string.IsNullOrWhiteSpace(metadataOutput))
{
    using (recorder.Measure(CoreMetricNames.FixtureMetadataWrite))
    {
        await FixtureGenerator.WriteMetadataJsonAsync(metadataOutput, count);
    }
}

if (!string.IsNullOrWhiteSpace(treeRoot))
{
    using (recorder.Measure(CoreMetricNames.FixtureTreeMaterialize))
    {
        await FixtureGenerator.MaterializeFileTreeAsync(treeRoot, count);
    }
}

await recorder.WriteJsonAsync(
    output,
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["kind"] = "core-baseline",
        ["fixture_asset_count"] = count.ToString(CultureInfo.InvariantCulture),
        ["fixture_digest_sha256"] = digest,
        ["metadata_output"] = metadataOutput ?? string.Empty,
        ["tree_root"] = treeRoot ?? string.Empty
    });

Console.WriteLine($"Lumine v2 Core benchmark: {count:N0} assets");
Console.WriteLine($"Fixture digest: {digest}");
Console.WriteLine($"Result: {Path.GetFullPath(output)}");
