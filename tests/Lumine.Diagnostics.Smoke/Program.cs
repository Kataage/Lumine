using System.Text.Json;
using Lumine.Diagnostics;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var first = FixtureGenerator.Create(0);
var last = FixtureGenerator.Create(99_999);
Require(first.Id == 1, "First fixture ID is not stable.");
Require(last.Id == 100_000, "100k fixture does not contain the expected last ID.");

var digestA = FixtureGenerator.ComputeDigest(100_000);
var digestB = FixtureGenerator.ComputeDigest(100_000);
Require(
    string.Equals(digestA, digestB, StringComparison.Ordinal),
    "100k fixture digest is not reproducible.");

var recorder = new BenchmarkRecorder();
using (recorder.Measure("smoke.operation"))
{
    _ = FixtureGenerator.ComputeDigest(1_000);
}

var tempRoot = Path.Combine(Path.GetTempPath(), "lumine-v2-diagnostics-smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    var treeRoot = Path.Combine(tempRoot, "tree");
    await FixtureGenerator.MaterializeFileTreeAsync(treeRoot, 128);
    var materialized = Directory.GetFiles(treeRoot, "*", SearchOption.AllDirectories);
    Require(materialized.Length == 128, $"Materialized {materialized.Length} files, expected 128.");

    var resultPath = Path.Combine(tempRoot, "result.json");
    await recorder.WriteJsonAsync(
        resultPath,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fixture_digest_sha256"] = digestA,
            ["fixture_asset_count"] = "100000"
        });

    await using var stream = File.OpenRead(resultPath);
    using var document = await JsonDocument.ParseAsync(stream);
    var root = document.RootElement;

    Require(root.GetProperty("schemaVersion").GetInt32() == 1, "Unexpected diagnostics schema.");
    Require(root.GetProperty("measurements").GetArrayLength() == 1, "Measurement was not serialized.");
    Require(
        !string.IsNullOrWhiteSpace(root.GetProperty("environment").GetProperty("hardwareId").GetString()),
        "Hardware ID was not captured.");
}
finally
{
    Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine($"100k deterministic fixture digest: {digestA}");
Console.WriteLine("Lumine v2 diagnostics smoke test passed.");
