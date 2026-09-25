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
    $firstViewportReady = Get-Metric $result "viewer.first_viewport_ready"
    $fastScroll = Get-Metric $result "viewer.fast_scroll_refresh"

    $realizedRows = [int]$result.metadata.max_realized_rows
    $attachedTiles = [int]$result.metadata.max_attached_tiles
    $maxReadyTiles = [int]$result.metadata.max_ready_tiles
    $maxDecodedEntries = [int]$result.metadata.max_decoded_bitmap_entries
    $maxDecodedBytes = [long]$result.metadata.max_decoded_bitmap_bytes
    $maxConcurrentDecodes = [int]$result.metadata.max_concurrent_bitmap_decodes
    $requests = [long]$result.metadata.thumbnail_requests
    $cancelled = [long]$result.metadata.thumbnail_requests_cancelled
    $requestFailures = [long]$result.metadata.thumbnail_requests_failed
    $tileFailures = [long]$result.metadata.tile_load_failures
    $lastTileError = [string]$result.metadata.last_tile_load_error
    $inflight = [int]$result.metadata.inflight_thumbnail_requests
    $finalAttached = [int]$result.metadata.final_attached_tiles
    $finalReady = [int]$result.metadata.final_ready_tiles
    $decodedEntries = [int]$result.metadata.decoded_bitmap_entries
    $decodedBytes = [long]$result.metadata.decoded_bitmap_bytes
    $activeDecodes = [int]$result.metadata.active_bitmap_decodes
    $peakDecodes = [int]$result.metadata.peak_concurrent_bitmap_decodes
    $decodeLimit = [int]$result.metadata.bitmap_decode_concurrency_limit
    $peak = [long]$result.metadata.peak_working_set_bytes
    $peakAdditional = [long]$result.metadata.peak_additional_working_set_bytes
    $scrollAllocated = [long]$fastScroll.after.totalAllocatedBytes - [long]$fastScroll.before.totalAllocatedBytes

    if ([double]$firstPaint.durationMs -gt 1500) {
        throw "$count Viewer first paint exceeded 1.5 s: $($firstPaint.durationMs) ms"
    }

    if ([double]$firstViewportReady.durationMs -gt 1500) {
        throw "$count Viewer first full viewport exceeded 1.5 s after first paint: $($firstViewportReady.durationMs) ms"
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

    if ($requestFailures -ne 0) {
        throw "$count Viewer thumbnail provider failures were observed: $requestFailures"
    }

    if ($tileFailures -ne 0) {
        throw "$count Viewer visible tile load failures were observed: $tileFailures; last=$lastTileError"
    }

    if ($inflight -ne 0) {
        throw "$count Viewer left thumbnail work in-flight after teardown: $inflight"
    }

    if ($finalAttached -ne 0) {
        throw "$count Viewer left realized tiles attached after teardown: $finalAttached"
    }

    if ($finalReady -ne 0) {
        throw "$count Viewer left ready bitmap tiles after teardown: $finalReady"
    }

    if ($decodedEntries -gt 64) {
        throw "$count Viewer decoded bitmap cache exceeded 64 entries: $decodedEntries"
    }

    if ($maxReadyTiles -le 0) {
        throw "$count Viewer benchmark never rendered an image-ready tile."
    }

    if ($maxDecodedEntries -gt 64) {
        throw "$count Viewer peak decoded bitmap entries exceeded 64: $maxDecodedEntries"
    }

    if ($maxDecodedBytes -gt 32MB) {
        throw "$count Viewer peak decoded bitmap cache exceeded 32 MiB: $maxDecodedBytes bytes"
    }

    if ($decodedBytes -gt 32MB) {
        throw "$count Viewer final decoded bitmap cache exceeded 32 MiB: $decodedBytes bytes"
    }

    if ($count -eq 100000 -and $maxDecodedEntries -lt 16) {
        throw "100k Viewer benchmark did not exercise decoded bitmap cache pressure: peak entries=$maxDecodedEntries"
    }

    if ($count -eq 100000 -and $maxDecodedBytes -lt 16MB) {
        throw "100k Viewer benchmark did not exercise realistic decoded memory pressure: peak=$maxDecodedBytes bytes"
    }

    if ($activeDecodes -ne 0) {
        throw "$count Viewer left bitmap decode work active after teardown: $activeDecodes"
    }

    if ($peakDecodes -lt 1 -or $peakDecodes -gt $decodeLimit) {
        throw "$count Viewer bitmap decode concurrency escaped bound: peak=$peakDecodes limit=$decodeLimit"
    }

    if ($maxConcurrentDecodes -gt $decodeLimit) {
        throw "$count Viewer observed decode concurrency above the hard limit: max=$maxConcurrentDecodes limit=$decodeLimit"
    }

    if ($decodeLimit -gt 2) {
        throw "$count Viewer bitmap decode concurrency limit is too high: $decodeLimit"
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

$cursorEnd = Get-Metric $hundredK "viewer.cursor_seek_end"
$cursorRandom = Get-Metric $hundredK "viewer.cursor_random_seek"
$cursorEndPages = [long]$hundredK.metadata.cursor_end_seek_pages
$cursorEndAllocated = [long]$cursorEnd.after.totalAllocatedBytes - [long]$cursorEnd.before.totalAllocatedBytes
$cursorRandomAllocated = [long]$cursorRandom.after.totalAllocatedBytes - [long]$cursorRandom.before.totalAllocatedBytes
$cursorRandomPages = [long]$hundredK.metadata.cursor_random_seek_pages
$cursorCachedPages = [int]$hundredK.metadata.cursor_cached_pages
$cursorCheckpoints = [int]$hundredK.metadata.cursor_checkpoints

if ([double]$cursorEnd.durationMs -gt 1500) {
    throw "100k cold cursor seek to end exceeded 1.5 s: $($cursorEnd.durationMs) ms"
}

if ([double]$cursorRandom.durationMs -gt 1500) {
    throw "100k cursor random-seek workload exceeded 1.5 s: $($cursorRandom.durationMs) ms"
}

if ($cursorEndAllocated -gt 64MB) {
    throw "100k cold cursor end seek allocated more than 64 MiB: $cursorEndAllocated bytes"
}

if ($cursorRandomAllocated -gt 64MB) {
    throw "100k random cursor seek allocated more than 64 MiB: $cursorRandomAllocated bytes"
}

if ($cursorEndPages -gt 400) {
    throw "100k cold cursor end seek fetched too many pages: $cursorEndPages"
}

if ($cursorRandomPages -gt 640) {
    throw "100k random cursor workload fetched too many additional pages: $cursorRandomPages"
}

if ($cursorCachedPages -gt 8) {
    throw "100k cursor integration exceeded metadata page cache bound: $cursorCachedPages"
}

if ($cursorCheckpoints -gt 128) {
    throw "100k cursor integration exceeded checkpoint bound: $cursorCheckpoints"
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
    $firstViewportReady = Get-Metric $result "viewer.first_viewport_ready"
    $fastScroll = Get-Metric $result "viewer.fast_scroll_refresh"
    $scrollAllocated = [long]$fastScroll.after.totalAllocatedBytes - [long]$fastScroll.before.totalAllocatedBytes

    Write-Host (
        "{0:N0}: first={1:N1} ms, first-full={2:N1} ms, scroll={3:N1} ms, rows={4}, tiles={5}, requests={6}, cancelled={7}, peak={8:N1} MiB, decoded-peak={9:N1} MiB, scroll alloc={10:N1} MiB" -f
        $count,
        $firstPaint.durationMs,
        $firstViewportReady.durationMs,
        $fastScroll.durationMs,
        $result.metadata.max_realized_rows,
        $result.metadata.max_attached_tiles,
        $result.metadata.thumbnail_requests,
        $result.metadata.thumbnail_requests_cancelled,
        ([long]$result.metadata.peak_working_set_bytes / 1MB),
        ([long]$result.metadata.max_decoded_bitmap_bytes / 1MB),
        ($scrollAllocated / 1MB))
}

Write-Host (
    "100k cursor integration: end={0:N1} ms/{1} pages/{2:N1} MiB alloc, random={3:N1} ms/{4} pages/{5:N1} MiB alloc, cache={6}, checkpoints={7}" -f
    $cursorEnd.durationMs,
    $cursorEndPages,
    ($cursorEndAllocated / 1MB),
    $cursorRandom.durationMs,
    $cursorRandomPages,
    ($cursorRandomAllocated / 1MB),
    $cursorCachedPages,
    $cursorCheckpoints)
