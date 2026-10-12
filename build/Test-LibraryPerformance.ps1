param(
    [string]$BenchmarkDirectory = "artifacts/benchmarks"
)

$ErrorActionPreference = "Stop"

function Read-LibraryBenchmark([int]$Count) {
    $path = Join-Path $BenchmarkDirectory "library-$Count.json"
    if (-not (Test-Path $path)) {
        throw "Missing Library benchmark result: $path"
    }

    return Get-Content $path -Raw | ConvertFrom-Json
}

function Get-Metric($Result, [string]$Name) {
    $matches = @($Result.measurements | Where-Object { $_.name -eq $Name })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one '$Name' metric, found $($matches.Count)."
    }

    return $matches[0]
}

$tenK = Read-LibraryBenchmark 10000
$fiftyK = Read-LibraryBenchmark 50000
$hundredK = Read-LibraryBenchmark 100000

$coldPath = Join-Path $BenchmarkDirectory "library-100000-cold.json"
if (-not (Test-Path $coldPath)) {
    throw "Missing cold 100k Library benchmark result: $coldPath"
}
$coldHundredK = Get-Content $coldPath -Raw | ConvertFrom-Json

$bulk50 = Get-Metric $fiftyK "library.bulk_upsert"
$bulk100 = Get-Metric $hundredK "library.bulk_upsert"
$coldBulk100 = Get-Metric $coldHundredK "library.bulk_upsert"
$reopen100 = Get-Metric $hundredK "library.database_reopen"
$query100 = Get-Metric $hundredK "library.query"
$keyset100 = Get-Metric $hundredK "library.keyset_traversal"
$metadataPersist100 = Get-Metric $hundredK "library.technical_metadata_persist"
$metadataBatch100 = Get-Metric $hundredK "library.technical_metadata_batch_persist"
$searchRebuild100 = Get-Metric $hundredK "library.search_index_rebuild"
$searchAscii100 = Get-Metric $hundredK "library.search_ascii"
$searchJapanese100 = Get-Metric $hundredK "library.search_japanese_short"
$searchTag100 = Get-Metric $hundredK "library.search_tag_filter"

$peakWorkingSet100 = [long]$hundredK.metadata.peak_working_set_bytes
$coldPeakWorkingSet100 = [long]$coldHundredK.metadata.peak_working_set_bytes
$maxPeakWorkingSet100 = [Math]::Max($peakWorkingSet100, $coldPeakWorkingSet100)
$peakAdditional100 = [long]$hundredK.metadata.peak_additional_working_set_bytes
$coldPeakAdditional100 = [long]$coldHundredK.metadata.peak_additional_working_set_bytes
$maxPeakAdditional100 = [Math]::Max($peakAdditional100, $coldPeakAdditional100)
$retainedWorkingSet100 = [long]$hundredK.metadata.retained_working_set_bytes
$coldRetainedWorkingSet100 = [long]$coldHundredK.metadata.retained_working_set_bytes
$maxRetainedWorkingSet100 = [Math]::Max($retainedWorkingSet100, $coldRetainedWorkingSet100)
$retainedAdditional100 = [long]$hundredK.metadata.retained_additional_working_set_bytes
$coldRetainedAdditional100 = [long]$coldHundredK.metadata.retained_additional_working_set_bytes
$maxRetainedAdditional100 = [Math]::Max($retainedAdditional100, $coldRetainedAdditional100)
$postGcHeap100 = [long]$hundredK.metadata.post_gc_heap_size_bytes
$coldPostGcHeap100 = [long]$coldHundredK.metadata.post_gc_heap_size_bytes
$maxPostGcHeap100 = [Math]::Max($postGcHeap100, $coldPostGcHeap100)
$ingestAllocated100 = [long]$hundredK.metadata.ingest_allocated_bytes
$coldIngestAllocated100 = [long]$coldHundredK.metadata.ingest_allocated_bytes
$maxIngestAllocated100 = [Math]::Max($ingestAllocated100, $coldIngestAllocated100)
$databaseBytes100 = [long]$hundredK.metadata.database_bytes

if ([int]$hundredK.metadata.traversed_asset_count -ne 100000) {
    throw "100k benchmark did not traverse all assets."
}

if ($hundredK.metadata.paging -ne "keyset:modified_at_utc_ticks,id") {
    throw "100k benchmark is not using the required keyset paging contract."
}

if ([int]$hundredK.metadata.technical_metadata_persist_count -ne 10000) {
    throw "100k benchmark did not persist technical metadata for the required 10,000-asset sample."
}

# Diagnostic only: print the critical phase split BEFORE the unchanged
# <=8s gate. On a red runner, the previous script lost this information
# because the summary footer was never reached. All fields come from the
# very same timed 10,000 single-asset production API calls.
$metadataPageCount = [int]$hundredK.metadata.technical_metadata_page_fetch_count
$metadataPageMs = [long]$hundredK.metadata.technical_metadata_page_fetch_ticks / 10000.0
$metadataWriteMs = [long]$hundredK.metadata.technical_metadata_update_ticks / 10000.0
$metadataMaxWriteMs = [long]$hundredK.metadata.technical_metadata_update_max_ticks / 10000.0
$metadataSlowWrites = [long]$hundredK.metadata.technical_metadata_update_over_8ms_count
if ($metadataPageCount -le 0 -or $metadataWriteMs -le 0 -or
    $metadataPageMs -lt 0 -or $metadataSlowWrites -lt 0) {
    throw "100k technical metadata timing-phase diagnostics are invalid."
}
Write-Host ("10k technical metadata phases: pages={0:N1} ms ({1:N0} fetches), writes={2:N1} ms, writes >=8ms={3:N0}, max={4:N1} ms (total={5:N1} ms)" -f
    $metadataPageMs, $metadataPageCount, $metadataWriteMs,
    $metadataSlowWrites, $metadataMaxWriteMs, [double]$metadataPersist100.durationMs)

# The phases below are timed inside the unchanged production repository
# transaction path. Keep all five fields and the 10k count mandatory;
# do not weaken the same <=8s acceptance gate.
if ([int]$hundredK.metadata.technical_metadata_phase_observed_write_count -ne 10000) {
    throw "100k benchmark did not measure every one of the 10,000 technical metadata writes."
}
$openMs = [long]$hundredK.metadata.technical_metadata_connection_open_ticks / 10000.0
$beginMs = [long]$hundredK.metadata.technical_metadata_begin_transaction_ticks / 10000.0
$updateMs = [long]$hundredK.metadata.technical_metadata_guarded_asset_update_ticks / 10000.0
$upsertMs = [long]$hundredK.metadata.technical_metadata_metadata_upsert_ticks / 10000.0
$commitMs = [long]$hundredK.metadata.technical_metadata_commit_ticks / 10000.0
if ($openMs -le 0 -or $beginMs -lt 0 -or $updateMs -le 0 -or
    $upsertMs -le 0 -or $commitMs -lt 0 -or
    ($openMs + $beginMs + $updateMs + $upsertMs + $commitMs) -gt ($metadataWriteMs + 10)) {
    throw "100k technical metadata in-repository phase timings are incomplete or inconsistent."
}
Write-Host ("10k technical metadata internal phases: pooled opens/PRAGMAs={0:N1} ms, BEGIN={1:N1} ms, guarded UPDATE={2:N1} ms, UPSERT={3:N1} ms, COMMIT={4:N1} ms" -f
    $openMs, $beginMs, $updateMs, $upsertMs, $commitMs)

# Exclusive production-path COMMIT duration distribution; zero is valid only
# for a particular bucket, never for the entire 10,000-write fixture.
$commitBuckets = @(
    [long]$hundredK.metadata.technical_metadata_commit_bucket_lt_1ms,
    [long]$hundredK.metadata.technical_metadata_commit_bucket_1_to_lt_8ms,
    [long]$hundredK.metadata.technical_metadata_commit_bucket_8_to_lt_32ms,
    [long]$hundredK.metadata.technical_metadata_commit_bucket_32_to_lt_128ms,
    [long]$hundredK.metadata.technical_metadata_commit_bucket_ge_128ms
)
$commitMaxMs = [long]$hundredK.metadata.technical_metadata_commit_max_ticks / 10000.0
$commitSlowMs = [long]$hundredK.metadata.technical_metadata_commit_over_8ms_ticks / 10000.0
$commitDistributionCount = ($commitBuckets | Measure-Object -Sum).Sum
if ($commitBuckets.Count -ne 5 -or
    $commitDistributionCount -ne 10000 -or
    ($commitBuckets | Where-Object { $_ -lt 0 }).Count -gt 0 -or
    $commitMaxMs -lt 0 -or $commitMaxMs -gt $commitMs -or
    $commitSlowMs -lt 0 -or $commitSlowMs -gt $commitMs -or
    ($commitBuckets[2] + $commitBuckets[3] + $commitBuckets[4]) -gt $metadataSlowWrites -or
    ($commitBuckets[2] + $commitBuckets[3] + $commitBuckets[4] -eq 0 -and $commitSlowMs -gt 0)) {
    throw "100k technical metadata SQLite COMMIT latency tail histogram is incomplete or inconsistent."
}
Write-Host ("10k individual SQLite COMMIT latency: <1ms={0:N0}, [1,8)ms={1:N0}, [8,32)ms={2:N0}, [32,128)ms={3:N0}, >=128ms={4:N0}; max={5:N1}ms, >=8ms cumulative={6:N1}ms (10,000 exclusive samples)" -f
    $commitBuckets[0], $commitBuckets[1], $commitBuckets[2],
    $commitBuckets[3], $commitBuckets[4], $commitMaxMs, $commitSlowMs)

$metadataBatchCount = [int]$hundredK.metadata.technical_metadata_batch_persist_count
$metadataBatchTransactions = [int]$hundredK.metadata.technical_metadata_batch_transaction_count
if ($metadataBatchCount -ne 10000 -or
    $metadataBatchTransactions -le 0 -or
    $metadataBatchTransactions -ge 10000 -or
    [double]$metadataBatch100.durationMs -le 0) {
    throw "100k bounded metadata batch did not persist all 10,000 records in fewer than 10,000 transactions."
}
Write-Host ("10k bounded metadata batch (warm second pass): {0:N1} ms, {1:N0} rows / {2:N0} commits" -f
    [double]$metadataBatch100.durationMs, $metadataBatchCount, $metadataBatchTransactions)

# Paired A-B-B-A diagnostic is run only on the already-warm 100k fixture.
# Both production APIs rewrite the same 10,000 source-guarded records twice,
# reversing order (single/batch/batch/single) to reduce monotonic cache bias.
# This is *not* a cold A/B test, and it MUST NOT substitute or soften the
# original 8s single-write gate below.
if ($hundredK.metadata.technical_metadata_warm_ab_enabled -ne "true") {
    throw "100k warm fixture omitted the required paired metadata A-B-B-A diagnostic."
}
$abRows = [int]$hundredK.metadata.technical_metadata_warm_ab_rows_per_phase
$abGroups = [int]$hundredK.metadata.technical_metadata_warm_ab_batch_groups_total
$expectedGroups = [int]([Math]::Ceiling($abRows / 64.0) * 2)
if ($abRows -ne 10000 -or $abGroups -ne $expectedGroups) {
    throw "Paired warm metadata A-B-B-A did not update all 10,000 guarded rows in both API modes (rows=$abRows, groups=$abGroups)."
}
$warmSingleFirst = Get-Metric $hundredK "library.technical_metadata_warm_ab_single_first"
$warmBatchFirst = Get-Metric $hundredK "library.technical_metadata_warm_ab_batch_first"
$warmBatchSecond = Get-Metric $hundredK "library.technical_metadata_warm_ab_batch_second"
$warmSingleSecond = Get-Metric $hundredK "library.technical_metadata_warm_ab_single_second"
foreach ($metric in @($warmSingleFirst, $warmBatchFirst, $warmBatchSecond, $warmSingleSecond)) {
    if ([double]$metric.durationMs -le 0) {
        throw "Paired warm metadata A-B-B-A reported a missing or invalid duration."
    }
}
Write-Host ("10k paired WARM A-B-B-A same-row production metadata: single1={0:N1} ms; batch1={1:N1} ms; batch2={2:N1} ms; single2={3:N1} ms; {4:N0} rows/phase, {5:N0} batch groups total (not cold A/B)" -f
    [double]$warmSingleFirst.durationMs, [double]$warmBatchFirst.durationMs,
    [double]$warmBatchSecond.durationMs, [double]$warmSingleSecond.durationMs,
    $abRows, $abGroups)

if ([double]$metadataPersist100.durationMs -gt 8000) {
    throw "10k technical metadata persistence exceeded 8 seconds: $($metadataPersist100.durationMs) ms"
}

if ([int]$hundredK.metadata.search_indexed_asset_count -ne 100000) {
    throw "100k benchmark did not rebuild the search index for every asset."
}

if ([double]$searchRebuild100.durationMs -gt 12000) {
    throw "100k search-index rebuild exceeded 12 seconds: $($searchRebuild100.durationMs) ms"
}

if ([double]$searchAscii100.durationMs -gt 250) {
    throw "100k ASCII partial search exceeded 250 ms: $($searchAscii100.durationMs) ms"
}

if ([double]$searchJapanese100.durationMs -gt 250) {
    throw "100k Japanese 2-char search exceeded 250 ms: $($searchJapanese100.durationMs) ms"
}

if ([double]$searchTag100.durationMs -gt 250) {
    throw "100k exact-tag/filter search exceeded 250 ms: $($searchTag100.durationMs) ms"
}

# Budgets intentionally include substantial hosted-runner headroom while still
# rejecting the previously observed 17-45 second write-path regressions.
if ([double]$coldBulk100.durationMs -gt 12000) {
    throw "Cold 100k bulk ingest exceeded 12 s: $($coldBulk100.durationMs) ms"
}

if ([double]$bulk100.durationMs -gt 10000) {
    throw "100k bulk ingest exceeded 10 s: $($bulk100.durationMs) ms"
}

if ([double]$bulk100.durationMs -gt (([double]$bulk50.durationMs * 3.0) + 1000.0)) {
    throw "100k bulk ingest scaled superlinearly beyond the accepted guard: 50k=$($bulk50.durationMs) ms, 100k=$($bulk100.durationMs) ms"
}

if ([double]$reopen100.durationMs -gt 1500) {
    throw "Existing 100k database reopen exceeded 1.5 s: $($reopen100.durationMs) ms"
}

if ([double]$query100.durationMs -gt 50) {
    throw "100k first-page query exceeded 50 ms: $($query100.durationMs) ms"
}

if ([double]$keyset100.durationMs -gt 1500) {
    throw "100k keyset traversal exceeded 1.5 s: $($keyset100.durationMs) ms"
}

Write-Host ("100k transient absolute peak working set: {0:N1} MiB" -f ($maxPeakWorkingSet100 / 1MB))
Write-Host ("100k transient incremental peak working set: {0:N1} MiB" -f ($maxPeakAdditional100 / 1MB))
Write-Host ("100k post-GC retained working set: {0:N1} MiB" -f ($maxRetainedWorkingSet100 / 1MB))
Write-Host ("100k post-GC retained increment: {0:N1} MiB" -f ($maxRetainedAdditional100 / 1MB))
Write-Host ("100k post-GC managed heap: {0:N1} MiB" -f ($maxPostGcHeap100 / 1MB))
Write-Host ("100k cumulative managed allocation: {0:N1} MiB" -f ($maxIngestAllocated100 / 1MB))

# Environment.WorkingSet is an OS physical-residency sample. On hosted
# Windows runners the same commit has shown >60 MiB swings after a full GC
# while the managed heap remained unchanged. Keep WorkingSet as an important
# diagnostic and retain only catastrophic hard ceilings here; use managed
# heap/allocation signals for deterministic Library-owned memory regression
# gates.
if ($maxPeakWorkingSet100 -gt 192MB) {
    throw "100k ingest transient absolute peak working set exceeded 192 MiB: $maxPeakWorkingSet100 bytes"
}

if ($maxPeakAdditional100 -gt 160MB) {
    throw "100k ingest transient working set added more than 160 MiB: $maxPeakAdditional100 bytes"
}

if ($maxRetainedWorkingSet100 -gt 192MB) {
    throw "100k ingest retained absolute working set exceeded 192 MiB after full GC: $maxRetainedWorkingSet100 bytes"
}

if ($maxPostGcHeap100 -gt 16MB) {
    throw "100k ingest retained managed heap exceeded 16 MiB after full GC: $maxPostGcHeap100 bytes"
}

if ($maxIngestAllocated100 -gt 512MB) {
    throw "100k ingest cumulative managed allocation exceeded 512 MiB: $maxIngestAllocated100 bytes"
}

if ($databaseBytes100 -gt 40MB) {
    throw "100k metadata database exceeded 40 MiB: $databaseBytes100 bytes"
}

Write-Host "Library performance acceptance passed."
Write-Host ("Cold 100k ingest: {0:N1} ms" -f $coldBulk100.durationMs)
Write-Host ("10k ingest: {0:N1} ms" -f (Get-Metric $tenK "library.bulk_upsert").durationMs)
Write-Host ("50k ingest: {0:N1} ms" -f $bulk50.durationMs)
Write-Host ("100k ingest: {0:N1} ms" -f $bulk100.durationMs)
Write-Host ("100k reopen: {0:N1} ms" -f $reopen100.durationMs)
Write-Host ("100k first-page query: {0:N1} ms" -f $query100.durationMs)
Write-Host ("100k keyset traversal: {0:N1} ms" -f $keyset100.durationMs)
Write-Host ("10k technical metadata persistence: {0:N1} ms" -f $metadataPersist100.durationMs)
Write-Host ("100k search-index rebuild: {0:N1} ms" -f $searchRebuild100.durationMs)
Write-Host ("100k ASCII partial search: {0:N1} ms" -f $searchAscii100.durationMs)
Write-Host ("100k Japanese 2-char search: {0:N1} ms" -f $searchJapanese100.durationMs)
Write-Host ("100k exact-tag/filter search: {0:N1} ms" -f $searchTag100.durationMs)
Write-Host ("100k database size: {0:N1} MiB" -f ($databaseBytes100 / 1MB))
