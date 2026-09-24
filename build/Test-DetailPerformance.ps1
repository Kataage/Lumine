param(
    [string]$Path = "artifacts/benchmarks/detail-full-resolution.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Path)) {
    throw "Detail full-resolution benchmark result not found: $Path"
}

$result = Get-Content $Path -Raw | ConvertFrom-Json
$required = [long]$result.metadata.required_rgba_bytes
$budget = [long]$result.metadata.configured_budget_bytes
$elapsed = [long]$result.metadata.decode_elapsed_ms
$peak = [long]$result.metadata.peak_working_set_bytes
$peakAdditional = [long]$result.metadata.peak_additional_working_set_bytes
$allocated = [long]$result.metadata.allocated_bytes
$stripes = [int]$result.metadata.stripe_count
$vipsFiles = [long]$result.metadata.vips_open_files
$vipsCache = [long]$result.metadata.vips_operation_cache_size

if ($required -le 64MB) {
    throw "Detail benchmark fixture is too small to exercise full-resolution pressure: $required bytes"
}

if ($required -gt $budget) {
    throw "Detail benchmark fixture exceeds configured decoded bitmap budget: required=$required budget=$budget"
}

if ($stripes -lt 2) {
    throw "Detail benchmark did not use striped decode: stripes=$stripes"
}

if ($elapsed -gt 10000) {
    throw "Detail full-resolution decode exceeded 10 seconds: $elapsed ms"
}

# One final RGBA bitmap plus bounded libvips/stripe overhead is expected.
# A second full-image buffer would push this well beyond this bound.
$peakLimit = [long]($required * 1.75) + 48MB
if ($peakAdditional -gt $peakLimit) {
    throw "Detail decode peak suggests duplicate/unbounded full-image residency: peak+$peakAdditional limit=$peakLimit required=$required"
}

if ($peak -gt 384MB) {
    throw "Detail full-resolution absolute peak exceeded 384 MiB: $peak bytes"
}

# Stripe byte[] allocations are cumulative, so total allocation can exceed one
# image, but should remain bounded to approximately one image plus overhead.
$allocationLimit = [long]($required * 1.5) + 32MB
if ($allocated -gt $allocationLimit) {
    throw "Detail decode managed allocation churn exceeded bound: allocated=$allocated limit=$allocationLimit"
}

if ($vipsFiles -ne 0) {
    throw "Detail decode left libvips source files open: $vipsFiles"
}

if ($vipsCache -ne 0) {
    throw "Detail decode left libvips operation cache entries: $vipsCache"
}

Write-Host "Detail full-resolution performance acceptance passed."
Write-Host ("Decode: {0:N0} ms" -f $elapsed)
Write-Host ("RGBA budget: {0:N1} MiB / {1:N1} MiB" -f ($required / 1MB), ($budget / 1MB))
Write-Host ("Peak additional working set: {0:N1} MiB" -f ($peakAdditional / 1MB))
Write-Host ("Managed allocation: {0:N1} MiB" -f ($allocated / 1MB))
