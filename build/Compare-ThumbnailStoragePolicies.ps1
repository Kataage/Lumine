param(
    [Parameter(Mandatory = $true)]
    [string]$Exe,

    [Parameter(Mandatory = $true)]
    [string]$Library,

    [string]$OutputDirectory = (Join-Path $PWD "artifacts/thumbnail-policy-comparison"),

    [int]$MinimumAssets = 1000,

    [int]$BrowseSeconds = 60,

    [int]$IdleSeconds = 10,

    [double]$TargetMaxFastScrollMs = 1500,

    [double]$CollectionMaxFastScrollMs = 30000,

    [string]$HardwareId = $env:COMPUTERNAME,

    [string]$Revision = "",

    [ValidateSet("MemoryFirst", "PersistentFirst")]
    [string]$PolicyOrder = "MemoryFirst",

    [ValidateSet("Default", "RedirectionSurface")]
    [string]$Win32CompositionMode = "Default",

    [ValidateSet("Default", "Software")]
    [string]$Win32RenderingMode = "Default"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $PSCommandPath
$acceptanceScript = Join-Path $scriptRoot "Run-RealLibraryAcceptance.ps1"

if (-not (Test-Path -LiteralPath $acceptanceScript)) {
    throw "Run-RealLibraryAcceptance.ps1 was not found next to this comparison runner."
}

$root = [IO.Path]::GetFullPath($OutputDirectory)
$persistentRoot = Join-Path $root "persistent-disk"
$memoryRoot = Join-Path $root "memory-only"
$comparisonPath = Join-Path $root "comparison.json"

New-Item -ItemType Directory -Force -Path $root | Out-Null

foreach ($path in @($persistentRoot, $memoryRoot)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

function Invoke-PolicyRun {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("PersistentDisk", "MemoryOnly")]
        [string]$StorageMode,

        [Parameter(Mandatory = $true)]
        [string]$PolicyOutput
    )

    $arguments = @{
        Exe = $Exe
        Library = $Library
        OutputDirectory = $PolicyOutput
        MinimumAssets = $MinimumAssets
        BrowseSeconds = $BrowseSeconds
        IdleSeconds = $IdleSeconds
        MaxFastScrollMs = $CollectionMaxFastScrollMs
        HardwareId = $HardwareId
        Win32CompositionMode = $Win32CompositionMode
        Win32RenderingMode = $Win32RenderingMode
        ThumbnailStorageMode = $StorageMode
    }

    if (-not [string]::IsNullOrWhiteSpace($Revision)) {
        $arguments["Revision"] = $Revision
    }

    & $acceptanceScript @arguments
}

function Read-Json {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Expected comparison result was not written: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
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

function Get-MeasurementValues {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    return @(
        $Result.measurements |
            Where-Object { $_.name -eq $Name } |
            ForEach-Object { [double]$_.durationMs }
    )
}

function Get-FirstMeasurement {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $values = @(Get-MeasurementValues -Result $Result -Name $Name)
    if ($values.Count -eq 0) {
        throw "Result did not contain measurement '$Name'."
    }

    return [double]$values[0]
}

function Get-ProcessCpuDeltaMs {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result
    )

    $measurements = @($Result.measurements | Sort-Object -Property startedAtUtc)
    if ($measurements.Count -eq 0) {
        return 0.0
    }

    $first = $measurements[0]
    $last = $measurements[-1]

    return [Math]::Max(
        0.0,
        [double]$last.after.totalProcessorTimeMs -
        [double]$first.before.totalProcessorTimeMs)
}

function Get-DirectionReversalTail {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result
    )

    $values = @(Get-MeasurementValues -Result $Result -Name "viewer.fast_scroll_refresh")
    if ($values.Count -lt 2) {
        return @($values)
    }

    return @(
        [double]$values[$values.Count - 2],
        [double]$values[$values.Count - 1]
    )
}

function Read-PolicyResult {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PolicyRoot
    )

    $summary = Read-Json -Path (Join-Path $PolicyRoot "summary.json")
    $cold = Read-Json -Path (Join-Path $PolicyRoot "cold.json")

    $steadyPath = Join-Path $PolicyRoot "warm-steady.json"
    $warmPath = if (Test-Path -LiteralPath $steadyPath) {
        $steadyPath
    }
    else {
        Join-Path $PolicyRoot "warm.json"
    }
    $warm = Read-Json -Path $warmPath

    return [pscustomobject]@{
        Summary = $summary
        Cold = $cold
        Warm = $warm
        WarmPath = $warmPath
    }
}

function Build-RunSummary {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result
    )

    $scroll = @(Get-MeasurementValues -Result $Result -Name "viewer.fast_scroll_refresh")
    $maxScroll = if ($scroll.Count -gt 0) {
        [double](($scroll | Measure-Object -Maximum).Maximum)
    }
    else {
        0.0
    }

    return [ordered]@{
        firstViewportMs = [Math]::Round(
            (Get-FirstMeasurement -Result $Result -Name "viewer.first_viewport_ready"),
            3)
        fastScrollMs = @(
            $scroll | ForEach-Object { [Math]::Round([double]$_, 3) }
        )
        maxFastScrollMs = [Math]::Round($maxScroll, 3)
        targetFastScrollPass = ($maxScroll -le $TargetMaxFastScrollMs)
        directionReversalTailMs = @(
            (Get-DirectionReversalTail -Result $Result) |
                ForEach-Object { [Math]::Round([double]$_, 3) }
        )
        processMeasuredCpuMs = [Math]::Round(
            (Get-ProcessCpuDeltaMs -Result $Result),
            3)
        peakWorkingSetBytes = [int64](Get-MetadataValue -Result $Result -Key "resource.peak_working_set_bytes")
        thumbnailSourceOpens = [int64](Get-MetadataValue -Result $Result -Key "thumbnail.source_opens")
        thumbnailSourceOpenCancellations = [int64](Get-MetadataValue -Result $Result -Key "thumbnail.source_open_cancellations")
        thumbnailGenerated = [int64](Get-MetadataValue -Result $Result -Key "thumbnail.generated")
        thumbnailCacheHits = [int64](Get-MetadataValue -Result $Result -Key "thumbnail.cache_hits")
        thumbnailMemoryCacheHits = [int64](Get-MetadataValue -Result $Result -Key "thumbnail.memory_cache_hits")
        thumbnailMemoryCacheBytes = [int64](Get-MetadataValue -Result $Result -Key "thumbnail.memory_cache_bytes")
        thumbnailMemoryCacheLimitBytes = [int64](Get-MetadataValue -Result $Result -Key "thumbnail.memory_cache_limit_bytes")
        thumbnailDiskFiles = [int64](Get-MetadataValue -Result $Result -Key "thumbnail.cache_after.files")
        thumbnailDiskBytes = [int64](Get-MetadataValue -Result $Result -Key "thumbnail.cache_after.bytes")
        metadataBytesHashed = [int64](Get-MetadataValue -Result $Result -Key "thumbnail.metadata_bytes_hashed")
        viewerCancelledRequests = [int64](Get-MetadataValue -Result $Result -Key "viewer.thumbnail_requests_cancelled")
        viewerTileFailures = [int64](Get-MetadataValue -Result $Result -Key "viewer.tile_load_failures")
    }
}

$runOrder =
    if ($PolicyOrder -eq "MemoryFirst") {
        @(
            [pscustomobject]@{
                Mode = "MemoryOnly"
                Output = $memoryRoot
            },
            [pscustomobject]@{
                Mode = "PersistentDisk"
                Output = $persistentRoot
            }
        )
    }
    else {
        @(
            [pscustomobject]@{
                Mode = "PersistentDisk"
                Output = $persistentRoot
            },
            [pscustomobject]@{
                Mode = "MemoryOnly"
                Output = $memoryRoot
            }
        )
    }

foreach ($run in $runOrder) {
    Write-Host ""
    Write-Host "=== Thumbnail storage policy comparison: $($run.Mode) ==="
    Invoke-PolicyRun -StorageMode $run.Mode -PolicyOutput $run.Output
}

$persistent = Read-PolicyResult -PolicyRoot $persistentRoot
$memory = Read-PolicyResult -PolicyRoot $memoryRoot

foreach ($property in @("appRevision", "hardwareId", "libraryPathSha256", "assetCount")) {
    if ([string]$persistent.Summary.$property -ne [string]$memory.Summary.$property) {
        throw "Policy comparison mismatch for '$property': PersistentDisk='$($persistent.Summary.$property)' MemoryOnly='$($memory.Summary.$property)'."
    }
}

$persistentCold = Build-RunSummary -Result $persistent.Cold
$persistentWarm = Build-RunSummary -Result $persistent.Warm
$memoryCold = Build-RunSummary -Result $memory.Cold
$memoryWarm = Build-RunSummary -Result $memory.Warm

$comparison = [ordered]@{
    schemaVersion = 2
    runOrder = @($runOrder | ForEach-Object { $_.Mode })
    appRevision = [string]$persistent.Summary.appRevision
    hardwareId = [string]$persistent.Summary.hardwareId
    libraryPathSha256 = [string]$persistent.Summary.libraryPathSha256
    assetCount = [int64]$persistent.Summary.assetCount
    targetMaxFastScrollMs = $TargetMaxFastScrollMs
    collectionMaxFastScrollMs = $CollectionMaxFastScrollMs
    persistentDisk = [ordered]@{
        cold = $persistentCold
        warm = $persistentWarm
        summary = $persistent.Summary
    }
    memoryOnly = [ordered]@{
        cold = $memoryCold
        warm = $memoryWarm
        summary = $memory.Summary
    }
    deltaMemoryMinusPersistent = [ordered]@{
        coldFirstViewportMs = [Math]::Round(
            [double]$memoryCold.firstViewportMs -
            [double]$persistentCold.firstViewportMs,
            3)
        coldMaxFastScrollMs = [Math]::Round(
            [double]$memoryCold.maxFastScrollMs -
            [double]$persistentCold.maxFastScrollMs,
            3)
        warmFirstViewportMs = [Math]::Round(
            [double]$memoryWarm.firstViewportMs -
            [double]$persistentWarm.firstViewportMs,
            3)
        warmMaxFastScrollMs = [Math]::Round(
            [double]$memoryWarm.maxFastScrollMs -
            [double]$persistentWarm.maxFastScrollMs,
            3)
        coldPeakWorkingSetBytes = [int64]$memoryCold.peakWorkingSetBytes -
            [int64]$persistentCold.peakWorkingSetBytes
        warmPeakWorkingSetBytes = [int64]$memoryWarm.peakWorkingSetBytes -
            [int64]$persistentWarm.peakWorkingSetBytes
        coldThumbnailDiskBytes = [int64]$memoryCold.thumbnailDiskBytes -
            [int64]$persistentCold.thumbnailDiskBytes
        warmThumbnailDiskBytes = [int64]$memoryWarm.thumbnailDiskBytes -
            [int64]$persistentWarm.thumbnailDiskBytes
    }
    interpretation = @(
        "MemoryOnly must report zero persistent thumbnail files and bytes.",
        "The first policy run is the only candidate for a machine-level cold read. The second policy can benefit from OS filesystem/page cache populated by the first run, so cross-policy Cold numbers are not symmetric unless the machine cache is reset between runs.",
        "PolicyOrder defaults to MemoryFirst because MemoryOnly is the product default; use PersistentFirst only for an intentional reverse-order diagnostic.",
        "PersistentDisk Warm measures cross-process persistent reuse; MemoryOnly Warm intentionally starts with no thumbnail persistence and therefore measures a fresh in-process memory-cache lifecycle on a warm library database.",
        "Both policies use the same libvips thumbnail profiles, source-identity rules, foreground/background scheduling, Viewer decoded-bitmap bounds and acceptance interaction sequence.",
        "The comparison runner collects with a permissive fast-scroll ceiling so both policies finish; targetFastScrollPass evaluates each run against TargetMaxFastScrollMs.",
        "Per-process OS disk-read bytes are not collected by the current Core diagnostics; source opens and metadata hash bytes are reported instead."
    )
}

$comparison |
    ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath $comparisonPath -Encoding UTF8

Write-Host ""
Write-Host "Thumbnail policy comparison complete."
Write-Host "Revision              : $($comparison.appRevision)"
Write-Host "Assets                : $($comparison.assetCount)"
Write-Host ("Persistent Cold max  : {0:N1} ms" -f $persistentCold.maxFastScrollMs)
Write-Host ("MemoryOnly Cold max  : {0:N1} ms" -f $memoryCold.maxFastScrollMs)
Write-Host ("Persistent Warm max  : {0:N1} ms" -f $persistentWarm.maxFastScrollMs)
Write-Host ("MemoryOnly Warm max  : {0:N1} ms" -f $memoryWarm.maxFastScrollMs)
Write-Host ("Persistent Cold disk : {0:N1} MiB" -f ([double]$persistentCold.thumbnailDiskBytes / 1MB))
Write-Host ("MemoryOnly Cold disk : {0:N1} MiB" -f ([double]$memoryCold.thumbnailDiskBytes / 1MB))
Write-Host "Comparison            : $comparisonPath"
