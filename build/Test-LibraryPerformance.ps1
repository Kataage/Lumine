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

if ([double]$metadataPersist100.durationMs -gt 8000) {
    throw "10k technical metadata persistence exceeded 8 seconds: $($metadataPersist100.durationMs) ms"
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

# Environment.WorkingSet includes transient physical pages committed for GC
# segments. Segment sizes are runtime implementation details, so a single
# segment commit must not masquerade as retained Library state. Keep a hard
# transient ceiling, then enforce the tighter historical budget against the
# post-full-GC retained process residency.
if ($maxPeakWorkingSet100 -gt 192MB) {
    throw "100k ingest transient absolute peak working set exceeded 192 MiB: $maxPeakWorkingSet100 bytes"
}

if ($maxPeakAdditional100 -gt 160MB) {
    throw "100k ingest transient working set added more than 160 MiB: $maxPeakAdditional100 bytes"
}

if ($maxRetainedWorkingSet100 -gt 160MB) {
    throw "100k ingest retained absolute working set exceeded 160 MiB after full GC: $maxRetainedWorkingSet100 bytes"
}

if ($maxRetainedAdditional100 -gt 96MB) {
    throw "100k ingest retained more than 96 MiB over process baseline after full GC: $maxRetainedAdditional100 bytes"
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
Write-Host ("100k database size: {0:N1} MiB" -f ($databaseBytes100 / 1MB))
