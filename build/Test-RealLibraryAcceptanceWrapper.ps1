param(
    [Parameter(Mandatory = $true)]
    [string]$NativeAotDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedRevision
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "Acceptance wrapper integration smoke requires Windows."
}

$nativeAotRoot = (Resolve-Path -LiteralPath $NativeAotDirectory).Path
$sourceExe = Join-Path $nativeAotRoot "Lumine.App.exe"
$sourceRevision = Join-Path $nativeAotRoot "revision.txt"
$wrapper = Join-Path $PSScriptRoot "Run-RealLibraryAcceptance.ps1"

foreach ($required in @($sourceExe, $sourceRevision, $wrapper)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Missing wrapper-smoke input: $required"
    }
}

$unicodeRootName = "Lumine " + [char]0x53D7 + [char]0x5165 + " wrapper"
$unicodeAppName = ([char]0x30A2).ToString() + [char]0x30D7 + [char]0x30EA + " build"
$unicodeLibraryName = ([char]0x753B).ToString() + [char]0x50CF + " library"

$root = Join-Path $env:RUNNER_TEMP $unicodeRootName
$app = Join-Path $root $unicodeAppName
$library = Join-Path $root $unicodeLibraryName
$output = Join-Path $root "acceptance result"

Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $app, $library, $output | Out-Null
Copy-Item -Path (Join-Path $nativeAotRoot "*") -Destination $app -Recurse -Force

$png = [Convert]::FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")
foreach ($index in 0..127) {
    [IO.File]::WriteAllBytes((Join-Path $library ("fixture-{0:D4}.png" -f $index)), $png)
}

$sentinel = Join-Path $library "source-sentinel.txt"
Set-Content -LiteralPath $sentinel -Value "preserve-source" -NoNewline -Encoding ascii

$overlapRejected = $false
try {
    & $wrapper -Exe (Join-Path $app "Lumine.App.exe") -Library $library -OutputDirectory $library -MinimumAssets 100 -BrowseSeconds 1 -IdleSeconds 1 -MaxFastScrollMs 30000
}
catch {
    if ($_.Exception.Message -notmatch "overlaps representative library|inside representative library") {
        throw
    }

    $overlapRejected = $true
}

if (-not $overlapRejected) {
    throw "Acceptance wrapper allowed source/output overlap."
}

if ((Get-Content -LiteralPath $sentinel -Raw) -ne "preserve-source") {
    throw "Overlap rejection modified the representative source library."
}

Set-Content -LiteralPath (Join-Path $output "cold.json") -Value '{"stale":true}' -Encoding ascii
Set-Content -LiteralPath (Join-Path $output "warm.json") -Value '{"stale":true}' -Encoding ascii
Set-Content -LiteralPath (Join-Path $output "summary.json") -Value '{"stale":true}' -Encoding ascii

& $wrapper -Exe (Join-Path $app "Lumine.App.exe") -Library $library -OutputDirectory $output -MinimumAssets 100 -BrowseSeconds 1 -IdleSeconds 1 -MaxFastScrollMs 30000

$summary = Get-Content -LiteralPath (Join-Path $output "summary.json") -Raw | ConvertFrom-Json

if ($summary.automatedDecision -ne "pass") {
    throw "Acceptance wrapper smoke did not produce a passing summary."
}

if ($summary.appRevision -ne $ExpectedRevision) {
    throw "Acceptance wrapper smoke lost build provenance: $($summary.appRevision)"
}

if ([int64]$summary.assetCount -ne 128) {
    throw "Acceptance wrapper smoke indexed $($summary.assetCount) assets instead of 128."
}

$coldHash = [string]$summary.rawResultSha256.cold
$warmHash = [string]$summary.rawResultSha256.warm
if ($coldHash -notmatch '^[0-9a-f]{64}$' -or $warmHash -notmatch '^[0-9a-f]{64}$') {
    throw "Acceptance wrapper smoke did not bind the summary to raw result hashes."
}

Write-Host "Real-library acceptance wrapper integration smoke passed."
Write-Host "Revision: $($summary.appRevision)"
Write-Host "Assets  : $($summary.assetCount)"
