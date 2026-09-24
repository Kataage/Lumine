param(
    [string]$BenchmarkPath = "artifacts/benchmarks/filesystem-sync.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BenchmarkPath)) {
    throw "Missing filesystem benchmark result: $BenchmarkPath"
}

$result = Get-Content $BenchmarkPath -Raw | ConvertFrom-Json

function Get-Metric([string]$Name) {
    $matches = @($result.measurements | Where-Object { $_.name -eq $Name })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one '$Name' metric, found $($matches.Count)."
    }

    return $matches[0]
}

$create = Get-Metric "filesystem.create_burst"
$modify = Get-Metric "filesystem.modify_burst"
$rename = Get-Metric "filesystem.rename_burst"
$delete = Get-Metric "filesystem.delete_burst"

$count = [int]$result.metadata.burst_count
$eventsObserved = [long]$result.metadata.events_observed
$overflows = [long]$result.metadata.overflows
$reconciliations = [long]$result.metadata.reconciliations
$reconcileFailures = [long]$result.metadata.reconcile_failures
$queueDepth = [int]$result.metadata.queue_depth
$maxLatency = [double]$result.metadata.max_apply_latency_ms
$peak = [long]$result.metadata.peak_working_set_bytes
$peakAdditional = [long]$result.metadata.peak_additional_working_set_bytes

if ($count -ne 128) {
    throw "Filesystem benchmark burst count changed unexpectedly: $count"
}

foreach ($metric in @($create, $modify, $rename, $delete)) {
    if ([double]$metric.durationMs -gt 5000) {
        throw "$($metric.name) exceeded 5 s: $($metric.durationMs) ms"
    }
}

if ($eventsObserved -lt $count) {
    throw "Filesystem watcher observed too few events: $eventsObserved for $count files"
}

if ($overflows -ne 0) {
    throw "Normal filesystem burst overflowed: $overflows"
}

if ($reconciliations -ne 0) {
    throw "Normal file-only filesystem burst fell back to full reconciliation: $reconciliations"
}

if ($reconcileFailures -ne 0) {
    throw "Filesystem benchmark reported reconciliation failures: $reconcileFailures"
}

if ($queueDepth -ne 0) {
    throw "Filesystem event queue did not drain: $queueDepth"
}

if ($maxLatency -gt 2500) {
    throw "Filesystem event-to-DB latency exceeded 2.5 s: $maxLatency ms"
}

if ($peak -gt 192MB) {
    throw "Filesystem benchmark absolute peak working set exceeded 192 MiB: $peak bytes"
}

if ($peakAdditional -gt 96MB) {
    throw "Filesystem benchmark added more than 96 MiB over process baseline: $peakAdditional bytes"
}

Write-Host "Filesystem performance acceptance passed."
Write-Host ("Create: {0:N1} ms" -f $create.durationMs)
Write-Host ("Modify: {0:N1} ms" -f $modify.durationMs)
Write-Host ("Rename: {0:N1} ms" -f $rename.durationMs)
Write-Host ("Delete: {0:N1} ms" -f $delete.durationMs)
Write-Host ("Max event-to-DB latency: {0:N1} ms" -f $maxLatency)
Write-Host ("Peak working set: {0:N1} MiB" -f ($peak / 1MB))
Write-Host ("Incremental peak: {0:N1} MiB" -f ($peakAdditional / 1MB))
