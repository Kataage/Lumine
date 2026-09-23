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

$peakWorkingSet100 = [long]$hundredK.metadata.peak_working_set_bytes
$coldPeakWorkingSet100 = [long]$coldHundredK.metadata.peak_working_set_bytes
$maxPeakWorkingSet100 = [Math]::Max($peakWorkingSet100, $coldPeakWorkingSet100)
$databaseBytes100 = [long]$hundredK.metadata.database_bytes

if ([int]$hundredK.metadata.traversed_asset_count -ne 100000) {
    throw "100k benchmark did not traverse all assets."
}

if ($hundredK.metadata.paging -ne "keyset:modified_at_utc_ticks,id") {
    throw "100k benchmark is not using the required keyset paging contract."
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

if ($maxPeakWorkingSet100 -gt 160MB) {
    throw "100k ingest peak working set exceeded 160 MiB: $maxPeakWorkingSet100 bytes"
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
Write-Host ("100k peak working set: {0:N1} MiB" -f ($maxPeakWorkingSet100 / 1MB))
Write-Host ("100k database size: {0:N1} MiB" -f ($databaseBytes100 / 1MB))
