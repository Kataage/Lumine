using System.Text.Json.Serialization;

namespace Lumine.Diagnostics;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(BenchmarkResult))]
[JsonSerializable(typeof(BenchmarkEnvironment))]
[JsonSerializable(typeof(BenchmarkMeasurement))]
[JsonSerializable(typeof(ResourceSnapshot))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(FixtureAsset))]
internal sealed partial class DiagnosticsJsonContext : JsonSerializerContext;
