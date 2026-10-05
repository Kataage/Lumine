param(
    [Parameter(Mandatory = $true)]
    [string]$NativeAotDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedRevision
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$publish =
    (Resolve-Path -LiteralPath $NativeAotDirectory).Path
$exe = Join-Path $publish "Lumine.App.exe"
$flag = Join-Path $publish "portable.flag"
$revision = Join-Path $publish "revision.txt"

foreach ($required in @($exe, $flag, $revision)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Acceptance package is missing required file: $required"
    }
}

$stampedRevision =
    (Get-Content -LiteralPath $revision -Raw).Trim()
if ($stampedRevision -ne $ExpectedRevision) {
    throw "Acceptance package revision mismatch. Expected $ExpectedRevision, found $stampedRevision."
}

$running =
    @(Get-Process -Name "Lumine.App" -ErrorAction SilentlyContinue)
if ($running.Count -ne 0) {
    $details =
        ($running | ForEach-Object {
            "PID=$($_.Id) StartTime=$($_.StartTime)"
        }) -join "; "
    throw "Lumine.App process remained after acceptance probes; refusing to sanitize around a lifecycle leak. $details"
}

$data = Join-Path $publish "data"
if (Test-Path -LiteralPath $data) {
    Remove-Item -LiteralPath $data -Recurse -Force
}

Start-Sleep -Milliseconds 500

if (Test-Path -LiteralPath $data) {
    throw "Acceptance package-local data directory reappeared after cleanup: $data"
}

$forbiddenNames =
    @(
        "instance.lock",
        "runtime.unclean",
        "runtime.log",
        "library.db",
        "library.db-wal",
        "library.db-shm"
    )
$forbidden =
    @(
        Get-ChildItem -LiteralPath $publish -Recurse -File |
            Where-Object {
                $forbiddenNames -contains $_.Name
            }
    )

if ($forbidden.Count -ne 0) {
    $paths =
        ($forbidden | ForEach-Object { $_.FullName }) -join [Environment]::NewLine
    throw "Acceptance package contains runtime state after sanitization:$([Environment]::NewLine)$paths"
}

Write-Host "NativeAOT acceptance package sanitized: no package-local runtime state remains."
