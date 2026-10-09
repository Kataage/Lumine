# Compare two actual Skia first-offset screenshot diagnostics from
# IDENTICAL synthetic fixture geometry; fail closed on any raster
# regression or resource-cap violation. Does not equate a headless PNG
# with the Windows GPU/compositor present fence.
param(
    [Parameter(Mandatory = $true)]
    [string]$ControlJson,
    [Parameter(Mandatory = $true)]
    [string]$TrialJson,
    [switch]$RequireRasterImprovement
)

$ErrorActionPreference = "Stop"

function Read-Frame([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label missing rendered-frame diagnostic JSON: $Path"
    }

    $result = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($null -eq $result.metadata) {
        throw "$Label missing diagnostic metadata."
    }

    $m = $result.metadata
    $keys = @(
        "asset_count", "prefetch_rows", "columns",
        "skia_frame_audit_columns",
        "skia_frame_blue_thumbnail_samples",
        "skia_frame_dark_placeholder_samples",
        "skia_frame_other_samples",
        "skia_frame_sample_y", "thumbnail_requests",
        "max_decoded_bitmap_bytes"
    )
    foreach ($key in $keys) {
        if ($null -eq $m.$key -or
            [string]$m.$key -eq "not-captured" -or
            [string]::IsNullOrWhiteSpace([string]$m.$key)) {
            throw "$Label missing or uncaptured $key; actual Skia screenshot required."
        }
    }

    return [pscustomobject]@{
        Count = [long]$m.asset_count
        PrefetchRows = [int]$m.prefetch_rows
        Columns = [int]$m.columns
        Samples = [int]$m.skia_frame_audit_columns
        Blue = [int]$m.skia_frame_blue_thumbnail_samples
        Dark = [int]$m.skia_frame_dark_placeholder_samples
        Other = [int]$m.skia_frame_other_samples
        Y = [int]$m.skia_frame_sample_y
        Requests = [long]$m.thumbnail_requests
        DecodedBytes = [long]$m.max_decoded_bitmap_bytes
    }
}

$control = Read-Frame $ControlJson "control"
$trial = Read-Frame $TrialJson "trial"

if ($control.Count -ne 10000 -or $trial.Count -ne 10000 -or
    $control.PrefetchRows -ne $trial.PrefetchRows -or
    $control.Columns -ne $trial.Columns -or
    $control.Y -ne $trial.Y -or
    $control.Columns -ne 7 -or
    $control.Y -ne 710 -or
    $control.Samples -ne $control.Columns -or
    $trial.Samples -ne $trial.Columns) {
    throw "Skia A/B fixture/geometry mismatch: compare identical 10k, 1200x800, 7-column first-offset frames."
}

foreach ($item in @($control, $trial)) {
    if ($item.Blue -lt 0 -or $item.Dark -lt 0 -or
        $item.Other -ne 0 -or
        ($item.Blue + $item.Dark + $item.Other) -ne $item.Samples) {
        throw "Skia rendered pixels were not fully accounted for as the known synthetic fixture."
    }
    if ($item.Requests -lt 0 -or $item.Requests -gt 1600) {
        throw "Skia A/B request budget exceeded: $($item.Requests) > 1600 for 10k assets."
    }
    if ($item.DecodedBytes -lt 0 -or
        $item.DecodedBytes -gt 32MB) {
        throw "Skia A/B decoded Bitmap budget exceeded: $($item.DecodedBytes) > 32 MiB."
    }
}

Write-Host (
    "Skia rendered first offset: control blue/dark={0}/{1}, trial={2}/{3}; requests={4}/{5}; decoded={6}/{7}." -f
    $control.Blue, $control.Dark, $trial.Blue, $trial.Dark,
    $control.Requests, $trial.Requests,
    $control.DecodedBytes, $trial.DecodedBytes)

if ($trial.Dark -gt $control.Dark -or
    $trial.Blue -lt $control.Blue) {
    throw "Skia raster REGRESSION: trial introduces more dark placeholders / fewer painted thumbnails."
}

if ($RequireRasterImprovement -and $control.Dark -gt 0 -and
    $trial.Dark -ge $control.Dark) {
    throw "Skia raster trial did not reduce visible dark placeholders despite headroom."
}

Write-Host "Skia raster and request/memory comparison accepted; this is NOT a Windows GPU-present acceptance."
