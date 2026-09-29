param(
    [Parameter(Mandatory = $true)]
    [string]$Exe,

    [Parameter(Mandatory = $true)]
    [string]$Library,

    [string]$OutputDirectory = (Join-Path $PWD "artifacts/real-library-acceptance"),

    [int]$MinimumAssets = 1000,

    [int]$BrowseSeconds = 60,

    [int]$IdleSeconds = 10,

    [double]$MaxFastScrollMs = 1500,

    [string]$HardwareId = $env:COMPUTERNAME,

    [string]$Revision = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (-not $IsWindows) {
    throw "Real-library Core acceptance requires Windows."
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw "Real-library Core acceptance requires Windows x64."
}

$exePath = (Resolve-Path -LiteralPath $Exe).Path
$libraryPath = (Resolve-Path -LiteralPath $Library).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$dataRoot = Join-Path $outputRoot "data"
$coldResultPath = Join-Path $outputRoot "cold.json"
$warmResultPath = Join-Path $outputRoot "warm.json"
$summaryPath = Join-Path $outputRoot "summary.json"

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

if (Test-Path -LiteralPath $dataRoot) {
    Remove-Item -LiteralPath $dataRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null

$oldHardware = $env:LUMINE_HARDWARE_ID
$oldRevision = $env:LUMINE_REVISION

if ([string]::IsNullOrWhiteSpace($HardwareId)) {
    $HardwareId = $env:COMPUTERNAME
}

$env:LUMINE_HARDWARE_ID = $HardwareId

if (-not [string]::IsNullOrWhiteSpace($Revision)) {
    $env:LUMINE_REVISION = $Revision
}

function Get-MetadataValue {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result,

        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    $property = $Result.metadata.PSObject.Properties[$Key]
    if ($null -eq $property) {
        return $null
    }

    return [string]$property.Value
}

function Get-MaxMeasurement {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $values = @(
        $Result.measurements |
            Where-Object { $_.name -eq $Name } |
            ForEach-Object { [double]$_.durationMs }
    )

    if ($values.Count -eq 0) {
        throw "Acceptance result did not contain measurement '$Name'."
    }

    return ($values | Measure-Object -Maximum).Maximum
}

function Get-Bottlenecks {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result
    )

    return @(
        $Result.measurements |
            Where-Object {
                $_.name -notin @(
                    "acceptance.long_browse",
                    "acceptance.idle_settle"
                )
            } |
            Sort-Object -Property durationMs -Descending |
            Select-Object -First 5 |
            ForEach-Object {
                [ordered]@{
                    name = $_.name
                    durationMs = [Math]::Round([double]$_.durationMs, 3)
                }
            }
    )
}

function Invoke-CoreAcceptance {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Mode,

        [Parameter(Mandatory = $true)]
        [string]$ResultPath
    )

    Write-Host ""
    Write-Host "=== Lumine real-library acceptance: $Mode ==="
    Write-Host "Library: $libraryPath"
    Write-Host "Output : $ResultPath"

    $acceptanceArgs = @(
        "--core-acceptance",
        "--library-dir=$libraryPath",
        "--data-dir=$dataRoot",
        "--output=$ResultPath",
        "--mode=$Mode",
        "--min-assets=$MinimumAssets",
        "--browse-seconds=$BrowseSeconds",
        "--idle-seconds=$IdleSeconds"
    )

    & $exePath @acceptanceArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Lumine.App acceptance process failed in $Mode mode with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $ResultPath)) {
        throw "Acceptance process did not write $ResultPath."
    }

    $marker = Join-Path $dataRoot "runtime.unclean"
    if (Test-Path -LiteralPath $marker) {
        throw "Clean $Mode acceptance left runtime.unclean behind."
    }

    $wal = Join-Path $dataRoot "library.db-wal"
    if ((Test-Path -LiteralPath $wal) -and ((Get-Item -LiteralPath $wal).Length -ne 0)) {
        throw "Clean $Mode acceptance left a non-empty SQLite WAL."
    }

    return (Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json)
}

try {
    $cold = Invoke-CoreAcceptance -Mode "cold" -ResultPath $coldResultPath
    $warm = Invoke-CoreAcceptance -Mode "warm" -ResultPath $warmResultPath

    foreach ($pair in @(
        @{ Name = "cold"; Result = $cold },
        @{ Name = "warm"; Result = $warm }
    )) {
        $mode = $pair.Name
        $result = $pair.Result

        if ((Get-MetadataValue -Result $result -Key "acceptance.automated_result") -ne "pass") {
            throw "$mode automated acceptance did not report pass."
        }

        $assetCount = [int64](Get-MetadataValue -Result $result -Key "library.asset_count")
        if ($assetCount -lt $MinimumAssets) {
            throw "$mode acceptance indexed only $assetCount assets."
        }

        $tileFailures = [int64](Get-MetadataValue -Result $result -Key "viewer.tile_load_failures")
        if ($tileFailures -ne 0) {
            throw "$mode acceptance recorded $tileFailures visible tile failures."
        }

        $thumbnailFailures = [int64](Get-MetadataValue -Result $result -Key "thumbnail.failed")
        if ($thumbnailFailures -ne 0) {
            throw "$mode acceptance recorded $thumbnailFailures thumbnail failures."
        }

        $interruptedWrites = [int64](Get-MetadataValue -Result $result -Key "thumbnail.cache_after.interrupted_writes")
        if ($interruptedWrites -ne 0) {
            throw "$mode acceptance left $interruptedWrites interrupted thumbnail writes."
        }

        $reconcileFailures = [int64](Get-MetadataValue -Result $result -Key "filesystem.reconcile_failures")
        if ($reconcileFailures -ne 0) {
            throw "$mode acceptance recorded $reconcileFailures filesystem reconcile failures."
        }

        $cacheBytes = [int64](Get-MetadataValue -Result $result -Key "thumbnail.cache_after.bytes")
        $cacheLimit = [int64](Get-MetadataValue -Result $result -Key "thumbnail.cache_configured_bytes")
        if ($cacheBytes -gt $cacheLimit) {
            throw "$mode thumbnail cache exceeded its configured disk budget before shutdown: $cacheBytes > $cacheLimit bytes."
        }

        $postShutdownCacheBytes = [int64](Get-MetadataValue -Result $result -Key "thumbnail.cache_post_shutdown.bytes")
        $postShutdownCacheLimit = [int64](Get-MetadataValue -Result $result -Key "thumbnail.cache_post_shutdown.configured_bytes")
        $postShutdownInterruptedWrites = [int64](Get-MetadataValue -Result $result -Key "thumbnail.cache_post_shutdown.interrupted_writes")

        if ($postShutdownCacheBytes -gt $postShutdownCacheLimit) {
            throw "$mode post-shutdown thumbnail cache exceeded its configured disk budget: $postShutdownCacheBytes > $postShutdownCacheLimit bytes."
        }

        if ($postShutdownInterruptedWrites -ne 0) {
            throw "$mode post-shutdown thumbnail cache retained $postShutdownInterruptedWrites interrupted write(s)."
        }

        $maxScroll = Get-MaxMeasurement -Result $result -Name "viewer.fast_scroll_refresh"
        if ($maxScroll -gt $MaxFastScrollMs) {
            throw "$mode fast-scroll refresh exceeded $MaxFastScrollMs ms: $([Math]::Round($maxScroll, 1)) ms."
        }
    }

    $warmSourceOpens = [int64](Get-MetadataValue -Result $warm -Key "thumbnail.source_opens")
    if ($warmSourceOpens -ne 0) {
        throw "Warm acceptance reopened original sources for thumbnail generation $warmSourceOpens time(s)."
    }

    $warmCacheHits = [int64](Get-MetadataValue -Result $warm -Key "thumbnail.cache_hits")
    if ($warmCacheHits -le 0) {
        throw "Warm acceptance did not observe persistent thumbnail cache hits."
    }

    $warmBootstrap = Get-MetadataValue -Result $warm -Key "filesystem.bootstrap_mode"
    $warnings = @()

    if ($warmBootstrap -ne "UsnDelta") {
        $warnings += "Warm filesystem bootstrap was '$warmBootstrap' rather than 'UsnDelta'. This can be valid when USN journal replay is unavailable, but should be reviewed for the target library volume."
    }

    $coldMaxScroll = Get-MaxMeasurement -Result $cold -Name "viewer.fast_scroll_refresh"
    $warmMaxScroll = Get-MaxMeasurement -Result $warm -Name "viewer.fast_scroll_refresh"

    $summary = [ordered]@{
        schemaVersion = 1
        automatedDecision = "pass"
        hardwareId = $HardwareId
        libraryPathSha256 = (Get-MetadataValue -Result $warm -Key "library.path_sha256")
        assetCount = [int64](Get-MetadataValue -Result $warm -Key "library.asset_count")
        cold = [ordered]@{
            maxFastScrollMs = [Math]::Round($coldMaxScroll, 3)
            peakWorkingSetBytes = [int64](Get-MetadataValue -Result $cold -Key "resource.peak_working_set_bytes")
            thumbnailSourceOpens = [int64](Get-MetadataValue -Result $cold -Key "thumbnail.source_opens")
            thumbnailCacheHits = [int64](Get-MetadataValue -Result $cold -Key "thumbnail.cache_hits")
            thumbnailCacheBytes = [int64](Get-MetadataValue -Result $cold -Key "thumbnail.cache_after.bytes")
            thumbnailCacheLimitBytes = [int64](Get-MetadataValue -Result $cold -Key "thumbnail.cache_configured_bytes")
            postShutdownThumbnailCacheBytes = [int64](Get-MetadataValue -Result $cold -Key "thumbnail.cache_post_shutdown.bytes")
            filesystemBootstrapMode = (Get-MetadataValue -Result $cold -Key "filesystem.bootstrap_mode")
        }
        warm = [ordered]@{
            maxFastScrollMs = [Math]::Round($warmMaxScroll, 3)
            peakWorkingSetBytes = [int64](Get-MetadataValue -Result $warm -Key "resource.peak_working_set_bytes")
            thumbnailSourceOpens = $warmSourceOpens
            thumbnailCacheHits = $warmCacheHits
            thumbnailCacheBytes = [int64](Get-MetadataValue -Result $warm -Key "thumbnail.cache_after.bytes")
            thumbnailCacheLimitBytes = [int64](Get-MetadataValue -Result $warm -Key "thumbnail.cache_configured_bytes")
            postShutdownThumbnailCacheBytes = [int64](Get-MetadataValue -Result $warm -Key "thumbnail.cache_post_shutdown.bytes")
            filesystemBootstrapMode = $warmBootstrap
        }
        measuredBottlenecks = [ordered]@{
            cold = @(Get-Bottlenecks -Result $cold)
            warm = @(Get-Bottlenecks -Result $warm)
        }
        knownLimitations = @(
            "Per-process OS disk-read byte counters are not collected; Core-owned thumbnail source-open and metadata-hash counters are recorded instead.",
            "Dedicated GPU-memory usage is not collected because the Core acceptance path owns no AI/GPU model runtime; compositor residency remains platform-owned.",
            "Automated timing/correctness cannot decide subjective visual comfort; the visible run still requires the manual observation checklist."
        )
        additionalFixIssues = @()
        acceptanceDecision = "automated-pass-manual-observation-required"
        warnings = $warnings
        manualObservation = [ordered]@{
            requiredBeforeClosingIssue295 = $true
            check = @(
                "Initial grid appears without an unpleasant stall.",
                "Continuous scroll and direction reversal remain visually responsive.",
                "Detail next/previous/zoom/1:1 interactions remain comfortable.",
                "No visible periodic whole-library stall occurs during the browse/idle window.",
                "No unresolved P0/P1 Core behavior is observed."
            )
        }
        rawResults = [ordered]@{
            cold = $coldResultPath
            warm = $warmResultPath
        }
    }

    $summary |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $summaryPath -Encoding UTF8

    Write-Host ""
    Write-Host "Automated real-library acceptance passed."
    Write-Host "Assets              : $($summary.assetCount)"
    Write-Host "Cold max fast-scroll: $($summary.cold.maxFastScrollMs) ms"
    Write-Host "Warm max fast-scroll: $($summary.warm.maxFastScrollMs) ms"
    Write-Host "Warm source opens    : $warmSourceOpens"
    Write-Host "Warm cache hits      : $warmCacheHits"
    Write-Host "Summary              : $summaryPath"

    if ($warnings.Count -gt 0) {
        Write-Warning ($warnings -join [Environment]::NewLine)
    }

    Write-Host ""
    Write-Host "Observe the visible Lumine windows during the run. #295 remains open until the manual usability observations in summary.json are confirmed."
}
finally {
    $env:LUMINE_HARDWARE_ID = $oldHardware
    $env:LUMINE_REVISION = $oldRevision
}
