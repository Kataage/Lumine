# Deterministic tests for the actual-rendered-pixel A/B gate.
# No GPU or Avalonia runtime needed: this tests the accept/reject contract
# against both types of prior failed Issue #634 experiments.
$ErrorActionPreference = "Stop"
$script = Join-Path $PSScriptRoot "Compare-ViewerSkiaRaster.ps1"
$root = Join-Path ([IO.Path]::GetTempPath()) ("lumine-raster-gate-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $root | Out-Null

function Write-Case(
    [string]$Name, [int]$Blue, [int]$Dark,
    [int]$Requests = 1400,
    [long]$Bytes = 33030144,
    [int]$Count = 10000,
    [int]$SampleY = 710,
    [int]$Other = 0
) {
    $path = Join-Path $root "$Name.json"
    $meta = @{
        asset_count = [string]$Count
        prefetch_rows = "2"
        columns = "7"
        skia_frame_audit_columns = "7"
        skia_frame_blue_thumbnail_samples = [string]$Blue
        skia_frame_dark_placeholder_samples = [string]$Dark
        skia_frame_other_samples = [string]$Other
        skia_frame_sample_y = [string]$SampleY
        thumbnail_requests = [string]$Requests
        max_decoded_bitmap_bytes = [string]$Bytes
    }
    @{ metadata = $meta } | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Expect([string]$Name, [bool]$Pass,
    [string]$Control, [string]$Trial)
{
    $failed = $false
    $message = ""
    try {
        & $script -ControlJson $Control -TrialJson $Trial -RequireRasterImprovement
    }
    catch {
        $failed = $true
        $message = $_.Exception.Message
    }

    if ($failed -eq $Pass) {
        throw "$Name: expected Pass=$Pass, got failure=$failed ($message)"
    }
    Write-Host "Raster gate test: $Name — expected pass=$Pass"
}

try {
    $control = Write-Case "control" 3 4
    Expect "real raster improvement" $true $control (Write-Case "improved" 5 2)
    Expect "same raster quality without improvement" $false $control (Write-Case "no-win" 3 4)
    Expect "all seven placeholders regression from observed PR654" $false $control (Write-Case "regressed" 0 7)
    Expect "source request over 1600" $false $control (Write-Case "too-many-requests" 5 2 1601)
    Expect "decoded 32MiB overflow" $false $control (Write-Case "too-many-bytes" 5 2 1400 33554433)
    Expect "invalid sample sum" $false $control (Write-Case "wrong-sum" 3 3)
    Expect "unexpected raster pixel color" $false $control (Write-Case "other-pixel" 4 2 1400 33030144 10000 710 1)
    Expect "different asset fixture" $false $control (Write-Case "different-count" 5 2 1400 33030144 50000)
    Expect "different sample position" $false $control (Write-Case "wrong-sample-y" 5 2 1400 33030144 10000 700)

    # If control is already perfect, parity cannot meaningfully improve,
    # and no-regression is the maximal enforceable raster condition.
    $perfect = Write-Case "perfect" 7 0
    Expect "already-perfect control remains perfect" $true $perfect (Write-Case "perfect-trial" 7 0)

    Write-Host "All ten deterministic Skia raster A/B policy tests passed."
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
