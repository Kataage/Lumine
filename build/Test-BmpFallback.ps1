param(
    [string]$BenchmarkPath = "artifacts/benchmarks/bmp-fallback.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BenchmarkPath)) {
    throw "Missing BMP fallback benchmark: $BenchmarkPath"
}

$result = Get-Content $BenchmarkPath -Raw | ConvertFrom-Json

function Get-Metric([string]$Name) {
    $matches = @($result.measurements | Where-Object { $_.name -eq $Name })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one '$Name' metric, found $($matches.Count)."
    }

    return $matches[0]
}

$thumbnail = Get-Metric "image.bmp_thumbnail_generate"
$full = Get-Metric "image.bmp_full_decode"

$width = [int]$result.metadata.fixture_width
$height = [int]$result.metadata.fixture_height
$sourceBytes = [long]$result.metadata.source_bytes
$cacheBytes = [long]$result.metadata.cache_bytes
$nativeBmpLoad = [bool]::Parse([string]$result.metadata.native_bmp_load)
$fallbackBmpLoad = [bool]::Parse([string]$result.metadata.fallback_bmp_load)
$thumbnailPeak = [long]$result.metadata.thumbnail_peak_working_set_bytes
$thumbnailPeakAdditional = [long]$result.metadata.thumbnail_peak_additional_working_set_bytes
$thumbnailAllocated = [long]$result.metadata.thumbnail_allocated_bytes
$fullPeak = [long]$result.metadata.full_peak_working_set_bytes
$fullPeakAdditional = [long]$result.metadata.full_peak_additional_working_set_bytes
$fullAllocated = [long]$result.metadata.full_allocated_bytes
$fullRows = [int]$result.metadata.full_rows
$newNativeDependencies = [int]$result.metadata.new_native_dependencies

if ($width -ne 4096 -or $height -ne 3072) {
    throw "BMP benchmark fixture dimensions changed unexpectedly: $width x $height"
}

if ($sourceBytes -lt 32MB) {
    throw "BMP benchmark fixture is not representative enough: $sourceBytes bytes"
}

if (-not $fallbackBmpLoad) {
    throw "Lumine BMP fallback capability is disabled."
}

if ($newNativeDependencies -ne 0) {
    throw "BMP fallback introduced unexpected native dependencies: $newNativeDependencies"
}

if ([double]$thumbnail.durationMs -gt 3000) {
    throw "BMP thumbnail fallback exceeded 3 s: $($thumbnail.durationMs) ms"
}

if ([double]$full.durationMs -gt 3000) {
    throw "BMP full-resolution fallback exceeded 3 s: $($full.durationMs) ms"
}

if ($thumbnailPeak -gt 256MB -or $thumbnailPeakAdditional -gt 192MB) {
    throw "BMP thumbnail fallback exceeded working-set budget: absolute=$thumbnailPeak additional=$thumbnailPeakAdditional"
}

if ($fullPeak -gt 256MB -or $fullPeakAdditional -gt 192MB) {
    throw "BMP full-resolution fallback exceeded working-set budget: absolute=$fullPeak additional=$fullPeakAdditional"
}

if ($thumbnailAllocated -gt 128MB) {
    throw "BMP thumbnail fallback allocated more than 128 MiB: $thumbnailAllocated"
}

if ($fullAllocated -gt 128MB) {
    throw "BMP full-resolution fallback allocated more than 128 MiB: $fullAllocated"
}

if ($cacheBytes -le 0) {
    throw "BMP thumbnail fallback produced an empty persistent cache file."
}

if ($fullRows -ne $height) {
    throw "BMP full-resolution fallback returned $fullRows rows; expected $height."
}

$imageProject = Get-Content "src/Lumine.Image/Lumine.Image.csproj" -Raw
if ($imageProject -match "ImageMagick|GraphicsMagick|Magick.NET|SkiaSharp|System.Drawing") {
    throw "BMP support added an unapproved heavyweight/image-stack dependency to Lumine.Image."
}

Write-Host "BMP fallback acceptance passed."
Write-Host ("Native libvips BMP loader: {0}" -f $nativeBmpLoad)
Write-Host ("Fallback BMP decoder: {0}" -f $fallbackBmpLoad)
Write-Host ("Source: {0:N1} MiB" -f ($sourceBytes / 1MB))
Write-Host ("Thumbnail: {0:N1} ms, +{1:N1} MiB peak, {2:N1} MiB allocated" -f $thumbnail.durationMs, ($thumbnailPeakAdditional / 1MB), ($thumbnailAllocated / 1MB))
Write-Host ("Full resolution: {0:N1} ms, +{1:N1} MiB peak, {2:N1} MiB allocated" -f $full.durationMs, ($fullPeakAdditional / 1MB), ($fullAllocated / 1MB))
