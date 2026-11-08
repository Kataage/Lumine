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
    throw 'Issue #634の実機確認には64ビットWindowsが必要です。'
}

$exePath = (Resolve-Path -LiteralPath $Exe -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf) -or
    -not [IO.Path]::GetFileName($exePath).Equals(
        'Lumine.App.exe', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Windows NativeAOTのポータブル版Lumine.App.exeを指定してください。'
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
    Write-Warning 'GPU情報を取得できませんでした。操作確認は続けられます。'
}

$sharedChecks = Join-Path $PSScriptRoot 'ViewerScrollReviewChecks.ps1'
if (-not (Test-Path -LiteralPath $sharedChecks -PathType Leaf)) {
    throw "スクロール確認用チェックリストが見つかりません: $sharedChecks"
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
    $lines.Add('# Lumine ビューアーの実機スクロール確認 — Issue #634')
    $lines.Add('')
    $lines.Add("ビルド識別子: $revision")
    $lines.Add("判定: $($report.manualDecision)")
    $lines.Add('CIの合格だけでは、実画面での使いやすさを確認したことになりません。')
    $lines.Add('')
    foreach ($item in $checks) {
        $mark = if ($item.status -eq 'pass') { 'x' } else { ' ' }
        $lines.Add("- [$mark] $($item.title) [判定: $($item.status)]")
        $lines.Add("  $($item.instruction)")
    }
    $lines.Add('')
    $lines.Add('viewer-scroll-owner-report.jsonまたは不合格の項目を共有してください。画像そのものは不要です。')
    $lines | Set-Content -LiteralPath $checklistPath -Encoding UTF8
}

Save-ReviewReport
if ($PrepareOnly) {
    Write-Host '準備のみ: Lumineは起動せず、手動検証も合格扱いにしません。'
    Write-Host "未確認レポート: $reportPath"
    return
}

Write-Host '=== Lumine スクロールの実機確認 — Issue #634 ==='
Write-Host "ビルド識別子: $revision"
Write-Host '実際の画像ライブラリで次の操作を確認し、終わったらLumineを閉じてください。'
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
if ($null -eq $process) { throw 'Lumineを起動できませんでした。実機確認は未完了です。' }
try {
    $process.WaitForExit()
    $report.appExitCode = $process.ExitCode
}
finally { $process.Dispose() }
if ($report.appExitCode -ne 0) {
    $report.manualDecision = 'fail'
    Save-ReviewReport
    throw "Lumineが異常終了しました（終了コード: $($report.appExitCode)）。実機確認は不合格です。"
}

function Ask-ReviewStatus {
    param([string]$Question)
    while ($true) {
        $answer = (Read-Host "$Question [y=問題なし / n=問題あり / s=未確認]").Trim().ToLowerInvariant()
        if ($answer -in @('y', 'yes')) { return 'pass' }
        if ($answer -in @('n', 'no')) { return 'fail' }
        if ($answer -in @('s', 'skip', '')) { return 'pending' }
        Write-Host 'y（問題なし）/ n（問題あり）/ s（未確認）から選んでください。'
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
Write-Host "スクロールの実機確認: $($report.manualDecision)"
Write-Host "結果レポート: $reportPath"
Write-Host "確認項目一覧: $checklistPath"
