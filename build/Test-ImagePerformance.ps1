param(
    [string]$BenchmarkPath = "artifacts/benchmarks/image-thumbnail.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BenchmarkPath)) {
    throw "Missing Image Core benchmark result: $BenchmarkPath"
}

$result = Get-Content $BenchmarkPath -Raw | ConvertFrom-Json

function Get-Metric([string]$Name) {
    $matches = @($result.measurements | Where-Object { $_.name -eq $Name })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one '$Name' metric, found $($matches.Count)."
    }

    return $matches[0]
}

$generate = Get-Metric "image.thumbnail_generate"
$hits = Get-Metric "image.thumbnail_cache_hit"
$batch = Get-Metric "image.thumbnail_batch_generate"
$metadataProbeBatch = Get-Metric "image.source_metadata_probe_batch"

$requestCount = [int]$result.metadata.batch_request_count
$workerCount = [int]$result.metadata.worker_count
$queueCapacity = [int]$result.metadata.queue_capacity
$cacheHits = [long]$result.metadata.cache_hits
$generated = [long]$result.metadata.generated
$sourceOpens = [long]$result.metadata.source_opens
$metadataProbes = [long]$result.metadata.metadata_probes
$metadataBytesHashed = [long]$result.metadata.metadata_bytes_hashed
$metadataMemoryHits = [long]$result.metadata.metadata_memory_hits
$metadataFastIdentityHits = [long]$result.metadata.metadata_fast_identity_hits
$metadataFullHashFallbacks = [long]$result.metadata.metadata_full_hash_fallbacks
$metadataProbeFixtureCount = [int]$result.metadata.metadata_probe_fixture_count
$metadataProbeFixtureBytes = [long]$result.metadata.metadata_probe_fixture_bytes
$metadataProbeFixtureFastIdentityHits = [int]$result.metadata.metadata_probe_fixture_fast_identity_hits
$metadataProbeFixtureFullHashFallbacks = [int]$result.metadata.metadata_probe_fixture_full_hash_fallbacks
$metadataProbeFixtureBytesHashed = [long]$result.metadata.metadata_probe_fixture_bytes_hashed
$cacheFiles = [long]$result.metadata.cache_files
$cacheBytes = [long]$result.metadata.cache_bytes
$batchPeak = [long]$result.metadata.batch_peak_working_set_bytes
$batchPeakAdditional = [long]$result.metadata.batch_peak_additional_working_set_bytes
$vipsOpenFiles = [int]$result.metadata.vips_open_files
$vipsOperationCacheSize = [int]$result.metadata.vips_operation_cache_size
$vipsConcurrency = [int]$result.metadata.vips_concurrency

if ($requestCount -ne 64) {
    throw "Image benchmark request count changed unexpectedly: $requestCount"
}

if ($workerCount -lt 1 -or $workerCount -gt 4) {
    throw "Thumbnail worker count escaped the 1..4 bound: $workerCount"
}

if ($queueCapacity -ne 64) {
    throw "Image benchmark queue capacity changed unexpectedly: $queueCapacity"
}

if ([double]$generate.durationMs -gt 750) {
    throw "Cold thumbnail generation exceeded 750 ms: $($generate.durationMs) ms"
}

if ([double]$hits.durationMs -gt 2000) {
    throw "1,000 persistent cache hits exceeded 2 s: $($hits.durationMs) ms"
}

if ([double]$batch.durationMs -gt 6000) {
    throw "64-request thumbnail batch exceeded 6 s: $($batch.durationMs) ms"
}

if ($cacheHits -ne 1000) {
    throw "Warm benchmark did not produce exactly 1,000 cache hits: $cacheHits"
}

if ($sourceOpens -ne $generated) {
    throw "Source-open invariant failed: opens=$sourceOpens generated=$generated"
}

if ($metadataProbes -ne $generated) {
    throw "Source metadata probe invariant failed: probes=$metadataProbes generated=$generated"
}

if (($metadataFastIdentityHits + $metadataFullHashFallbacks) -ne $metadataProbes) {
    throw "Source identity accounting mismatch: probes=$metadataProbes fast=$metadataFastIdentityHits hash=$metadataFullHashFallbacks"
}

if ($metadataFullHashFallbacks -eq 0 -and $metadataBytesHashed -ne 0) {
    throw "Fast source identity path unexpectedly hashed bytes: $metadataBytesHashed"
}

if ($metadataFullHashFallbacks -gt 0 -and $metadataBytesHashed -le 0) {
    throw "Hash fallback occurred without recorded hashed bytes."
}

if ($metadataMemoryHits -ne 1000) {
    throw "Warm thumbnail benchmark did not reuse bounded in-session source metadata exactly 1,000 times: $metadataMemoryHits"
}

if ($metadataProbeFixtureCount -ne 10000 -or $metadataProbeFixtureBytes -le 0) {
    throw "Source metadata probe fixture is incomplete: count=$metadataProbeFixtureCount bytes=$metadataProbeFixtureBytes"
}

if (($metadataProbeFixtureFastIdentityHits + $metadataProbeFixtureFullHashFallbacks) -ne 10000) {
    throw "10k distinct source identity accounting mismatch: fast=$metadataProbeFixtureFastIdentityHits hash=$metadataProbeFixtureFullHashFallbacks"
}

if ($metadataProbeFixtureFullHashFallbacks -eq 0 -and $metadataProbeFixtureBytesHashed -ne 0) {
    throw "10k fast identity fixture unexpectedly hashed bytes: $metadataProbeFixtureBytesHashed"
}

if ($metadataProbeFixtureFullHashFallbacks -gt 0 -and $metadataProbeFixtureBytesHashed -le 0) {
    throw "10k hash fallback fixture did not record hashed bytes."
}

if ([double]$metadataProbeBatch.durationMs -gt 30000) {
    throw "10k source metadata probes exceeded 30 seconds: $($metadataProbeBatch.durationMs) ms"
}

if ($generated -ne 65 -or $cacheFiles -ne 65) {
    throw "Expected 65 generated/cache files after benchmark: generated=$generated files=$cacheFiles"
}

if ($cacheBytes -le 0) {
    throw "Image cache benchmark produced zero persisted bytes."
}

if ($batchPeak -gt 192MB) {
    throw "Thumbnail batch absolute peak working set exceeded 192 MiB: $batchPeak bytes"
}

if ($batchPeakAdditional -gt 160MB) {
    throw "Thumbnail batch added more than 160 MiB over process baseline: $batchPeakAdditional bytes"
}

if ($vipsOpenFiles -ne 0) {
    throw "libvips retained source/cache file handles after benchmark work: $vipsOpenFiles"
}

if ($vipsOperationCacheSize -ne 0) {
    throw "libvips operation cache is not disabled: size=$vipsOperationCacheSize"
}

if ($vipsConcurrency -lt 1 -or $vipsConcurrency -gt 4) {
    throw "libvips concurrency escaped the 1..4 Image Core bound: $vipsConcurrency"
}

if (($workerCount * $vipsConcurrency) -gt [Math]::Max(4, [Environment]::ProcessorCount)) {
    throw "Combined thumbnail worker/libvips concurrency is oversubscribed: workers=$workerCount vips=$vipsConcurrency CPUs=$([Environment]::ProcessorCount)"
}

Write-Host "Image performance acceptance passed."
Write-Host ("Cold generation: {0:N1} ms" -f $generate.durationMs)
Write-Host ("1,000 cache hits: {0:N1} ms" -f $hits.durationMs)
Write-Host ("64-request batch: {0:N1} ms" -f $batch.durationMs)
Write-Host ("10k distinct source metadata probes: {0:N1} ms ({1:N1} MiB logical files, fast={2}, hash-fallback={3}, hashed={4:N1} MiB)" -f $metadataProbeBatch.durationMs, ($metadataProbeFixtureBytes / 1MB), $metadataProbeFixtureFastIdentityHits, $metadataProbeFixtureFullHashFallbacks, ($metadataProbeFixtureBytesHashed / 1MB))
Write-Host ("Batch peak working set: {0:N1} MiB" -f ($batchPeak / 1MB))
Write-Host ("Batch incremental peak: {0:N1} MiB" -f ($batchPeakAdditional / 1MB))
Write-Host ("libvips open files after batch: {0}" -f $vipsOpenFiles)
Write-Host ("libvips operation cache size: {0}" -f $vipsOperationCacheSize)
Write-Host ("Source metadata probes: {0} (fast={1}, hash-fallback={2}, {3:N1} MiB hashed, {4} memory hits)" -f $metadataProbes, $metadataFastIdentityHits, $metadataFullHashFallbacks, ($metadataBytesHashed / 1MB), $metadataMemoryHits)
Write-Host ("libvips concurrency: {0} (workers={1})" -f $vipsConcurrency, $workerCount)
