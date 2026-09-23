param(
    [string]$BenchmarkDirectory = "artifacts/benchmarks"
)

$ErrorActionPreference = "Stop"

function Read-ViewerBenchmark([int]$Count) {
    $path = Join-Path $BenchmarkDirectory "viewer-$Count.json"
    if (-not (Test-Path $path)) {
        throw "Missing Viewer benchmark result: $path"
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

$tenK = Read-ViewerBenchmark 10000
$fiftyK = Read-ViewerBenchmark 50000
$hundredK = Read-ViewerBenchmark 100000

$results = @($tenK, $fiftyK, $hundredK)

foreach ($result in $results) {
    $count = [int]$result.metadata.asset_count
    $firstPaint = Get-Metric $result "viewer.first_paint"
    $fastScroll = Get-Metric $result "viewer.fast_scroll_refresh"

    $realizedRows = [int]$result.metadata.max_realized_rows
    $attachedTiles = [int]$result.metadata.max_attached_tiles
    $requests = [long]$result.metadata.thumbnail_requests
    $cancelled = [long]$result.metadata.thumbnail_requests_cancelled
    $inflight = [int]$result.metadata.inflight_thumbnail_requests
    $decodedEntries = [int]$result.metadata.decoded_bitmap_entries
    $decodedBytes = [long]$result.metadata.decoded_bitmap_bytes
    $peak = [long]$result.metadata.peak_working_set_bytes
    $peakAdditional = [long]$result.metadata.peak_additional_working_set_bytes
    $scrollAllocated = [long]$fastScroll.after.totalAllocatedBytes - [long]$fastScroll.before.totalAllocatedBytes

    if ([double]$firstPaint.durationMs -gt 1500) {
        throw "$count Viewer first paint exceeded 1.5 s: $($firstPaint.durationMs) ms"
    }

    if ([double]$fastScroll.durationMs -gt 1500) {
        throw "$count Viewer fast-scroll refresh exceeded 1.5 s: $($fastScroll.durationMs) ms"
    }

    if ($realizedRows -gt 16) {
        throw "$count Viewer realized too many rows: $realizedRows"
    }

    if ($attachedTiles -gt 128) {
        throw "$count Viewer attached too many tiles: $attachedTiles"
    }

    if ($requests -gt 1600) {
        throw "$count Viewer issued too many thumbnail requests during fast-scroll workload: $requests"
    }

    if ($cancelled -le 0) {
        throw "$count Viewer did not cancel stale thumbnail work."
    }

    if ($inflight -ne 0) {
        throw "$count Viewer left thumbnail work in-flight: $inflight"
    }

    if ($decodedEntries -gt 64) {
        throw "$count Viewer decoded bitmap cache exceeded 64 entries: $decodedEntries"
    }

    if ($decodedBytes -gt 32MB) {
        throw "$count Viewer decoded bitmap cache exceeded 32 MiB: $decodedBytes bytes"
    }

    if ($peak -gt 160MB) {
        throw "$count Viewer absolute peak working set exceeded 160 MiB: $peak bytes"
    }

    if ($peakAdditional -gt 128MB) {
        throw "$count Viewer incremental peak working set exceeded 128 MiB: $peakAdditional bytes"
    }

    if ($scrollAllocated -gt 96MB) {
        throw "$count Viewer fast-scroll allocated more than 96 MiB: $scrollAllocated bytes"
    }
}

$rows10 = [int]$tenK.metadata.max_realized_rows
$rows100 = [int]$hundredK.metadata.max_realized_rows
$tiles10 = [int]$tenK.metadata.max_attached_tiles
$tiles100 = [int]$hundredK.metadata.max_attached_tiles
$peak10 = [long]$tenK.metadata.peak_working_set_bytes
$peak100 = [long]$hundredK.metadata.peak_working_set_bytes

if ($rows100 -gt ($rows10 + 2)) {
    throw "100k realized row count scaled with library size: 10k=$rows10, 100k=$rows100"
}

if ($tiles100 -gt ($tiles10 + 14)) {
    throw "100k attached tile count scaled with library size: 10k=$tiles10, 100k=$tiles100"
}

if ($peak100 -gt ($peak10 + 32MB)) {
    throw "100k Viewer peak memory scaled excessively versus 10k: 10k=$peak10, 100k=$peak100"
}

Write-Host "Viewer performance acceptance passed."
foreach ($result in $results) {
    $count = [int]$result.metadata.asset_count
    $firstPaint = Get-Metric $result "viewer.first_paint"
    $fastScroll = Get-Metric $result "viewer.fast_scroll_refresh"
    $scrollAllocated = [long]$fastScroll.after.totalAllocatedBytes - [long]$fastScroll.before.totalAllocatedBytes

    Write-Host (
        "{0:N0}: first={1:N1} ms, scroll={2:N1} ms, rows={3}, tiles={4}, requests={5}, cancelled={6}, peak={7:N1} MiB, scroll alloc={8:N1} MiB" -f
        $count,
        $firstPaint.durationMs,
        $fastScroll.durationMs,
        $result.metadata.max_realized_rows,
        $result.metadata.max_attached_tiles,
        $result.metadata.thumbnail_requests,
        $result.metadata.thumbnail_requests_cancelled,
        ([long]$result.metadata.peak_working_set_bytes / 1MB),
        ($scrollAllocated / 1MB))
}
