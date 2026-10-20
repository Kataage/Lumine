param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\artifacts\native-aot\Lumine.App.exe'),
    [string]$OutputDirectory = (Join-Path $PWD 'artifacts\viewer-scroll-owner-review'),
    [string]$DataDirectory = '',
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or
    -not [Environment]::Is64BitOperatingSystem) {
    throw 'Issue #634 owner review requires Windows x64.'
}

$exePath = (Resolve-Path -LiteralPath $Exe -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf) -or
    -not [IO.Path]::GetFileName($exePath).Equals(
        'Lumine.App.exe', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use Lumine.App.exe from the Windows NativeAOT portable package.'
}

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$reportPath = Join-Path $outputRoot 'viewer-scroll-owner-report.json'
$checklistPath = Join-Path $outputRoot 'VIEWER-SCROLL-CHECKLIST.md'
$revisionPath = Join-Path (Split-Path -Parent $exePath) 'revision.txt'
$revision = 'unknown'
if (Test-Path -LiteralPath $revisionPath -PathType Leaf) {
    $revision = (Get-Content -LiteralPath $revisionPath -Raw).Trim()
}

# Hardware inventory is informational. GPU adapter names cannot prove
# compositor present, no blank frames, or actual GPU decoding.
$gpuNames = @()
try {
    $gpuNames = @(Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) } |
        ForEach-Object { [string]$_.Name } | Sort-Object -Unique)
}
catch {
    Write-Warning 'Video controller inventory unavailable; review can still be completed.'
}

$sharedChecks = Join-Path $PSScriptRoot 'ViewerScrollReviewChecks.ps1'
if (-not (Test-Path -LiteralPath $sharedChecks -PathType Leaf)) {
    throw "Missing shared Viewer scroll checklist: $sharedChecks"
}
. $sharedChecks
$checks = @(New-ViewerScrollReviewChecks)

$softwareOverride = [string]::Equals(
    [Environment]::GetEnvironmentVariable('LUMINE_WIN32_RENDERING_MODE'),
    'Software', [StringComparison]::OrdinalIgnoreCase)

$report = [ordered]@{
    schemaVersion = 1
    issue = 634
    evidenceClass = 'human-self-report-not-gpu-present-timing'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    appRevision = $revision
    preparationOnly = [bool]$PrepareOnly
    osVersion = [Environment]::OSVersion.VersionString
    videoControllerNames = @($gpuNames)
    softwareRendererOverride = $softwareOverride
    thumbnailStorageMode = 'MemoryOnly'
    appExitCode = $null
    manualDecision = 'pending'
    checks = $checks
    notes = @(
        'No physical frame-present measurement is collected.',
        'CI headless Skia success does not establish real Windows product quality.',
        'No private library paths, filenames or image content are included.'
    )
}

function Save-ReviewReport {
    $report.generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $report | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $reportPath -Encoding UTF8

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# Lumine Viewer scroll owner review - Issue #634')
    $lines.Add('')
    $lines.Add("Revision: $revision")
    $lines.Add("Decision: $($report.manualDecision)")
    $lines.Add('Headless CI passes are not physical owner acceptance.')
    $lines.Add('')
    foreach ($item in $checks) {
        $mark = if ($item.status -eq 'pass') { 'x' } else { ' ' }
        $lines.Add("- [$mark] $($item.title) [status: $($item.status)]")
        $lines.Add("  $($item.instruction)")
    }
    $lines.Add('')
    $lines.Add('Send viewer-scroll-owner-report.json or the failing item; do not send private images.')
    $lines | Set-Content -LiteralPath $checklistPath -Encoding UTF8
}

Save-ReviewReport
if ($PrepareOnly) {
    Write-Host 'PREPARE ONLY: no application launched and no manual pass claimed.'
    Write-Host "Pending report: $reportPath"
    return
}

Write-Host '=== Lumine physical Windows scroll review - Issue #634 ==='
Write-Host "Revision: $revision"
Write-Host 'Use a representative real library. Perform these checks, then close Lumine:'
foreach ($item in $checks) { Write-Host " - $($item.title): $($item.instruction)" }

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $exePath
$startInfo.WorkingDirectory = Split-Path -Parent $exePath
$startInfo.UseShellExecute = $false
$startInfo.EnvironmentVariables['LUMINE_THUMBNAIL_STORAGE_MODE'] = 'MemoryOnly'
if (-not [string]::IsNullOrWhiteSpace($DataDirectory)) {
    # Opt-in only: never silently overwrite or reuse the existing profile.
    $startInfo.EnvironmentVariables['LUMINE_DATA_DIR'] =
        [IO.Path]::GetFullPath($DataDirectory)
}

$process = [System.Diagnostics.Process]::Start($startInfo)
if ($null -eq $process) { throw 'Lumine failed to launch; review remains pending.' }
try {
    $process.WaitForExit()
    $report.appExitCode = $process.ExitCode
}
finally { $process.Dispose() }
if ($report.appExitCode -ne 0) {
    $report.manualDecision = 'fail'
    Save-ReviewReport
    throw "Lumine exited with $($report.appExitCode); review did not pass."
}

function Ask-ReviewStatus {
    param([string]$Question)
    while ($true) {
        $answer = (Read-Host "$Question [y/n/s=skip]").Trim().ToLowerInvariant()
        if ($answer -in @('y', 'yes')) { return 'pass' }
        if ($answer -in @('n', 'no')) { return 'fail' }
        if ($answer -in @('s', 'skip', '')) { return 'pending' }
        Write-Host 'Choose y, n or s.'
    }
}

foreach ($item in $checks) {
    $item.status = Ask-ReviewStatus -Question $item.title
    Save-ReviewReport
}

$failed = @($checks | Where-Object { $_.status -eq 'fail' }).Count
$pending = @($checks | Where-Object { $_.status -eq 'pending' }).Count
if ($failed -gt 0) { $report.manualDecision = 'fail' }
elseif ($pending -gt 0) { $report.manualDecision = 'pending' }
elseif ($softwareOverride) { $report.manualDecision = 'not-qualified-software-renderer' }
else {
    # Human self-report, not automatic physical GPU frame-present proof.
    # Issue closure still requires explicit owner review of this report.
    $report.manualDecision = 'owner-reported-pass'
}
Save-ReviewReport
Write-Host "Viewer scroll review: $($report.manualDecision)"
Write-Host "Report: $reportPath"
Write-Host "Checklist: $checklistPath"
