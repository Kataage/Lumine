# Issue #801: verify real Japanese user-facing prompts under Windows PowerShell 5.1.
# This test does not launch the app or alter manual review verdicts.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$files = @(
    'Run-ProductAcceptance.ps1',
    'ViewerScrollReviewChecks.ps1',
    'Run-ViewerScrollOwnerReview.ps1'
)

foreach ($file in $files) {
    $sourcePath = Join-Path $PSScriptRoot $file
    $bytes = [IO.File]::ReadAllBytes($sourcePath)
    # Windows PowerShell 5.1 decodes BOM-less UTF-8 as the system ANSI code page.
    if ($bytes.Length -lt 3 -or
        $bytes[0] -ne 0xef -or $bytes[1] -ne 0xbb -or $bytes[2] -ne 0xbf) {
        throw "UTF-8 BOM is required for Windows PowerShell 5.1: $file"
    }

    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile(
        $sourcePath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -ne 0) {
        throw "PowerShell syntax failed for $file: $($errors[0])"
    }

    $text = [IO.File]::ReadAllText(
        $sourcePath, [Text.Encoding]::UTF8)
    if (-not $text.Contains('確認')) {
        throw "Japanese review wording is missing: $file"
    }
}

$main = [IO.File]::ReadAllText(
    (Join-Path $PSScriptRoot 'Run-ProductAcceptance.ps1'),
    [Text.Encoding]::UTF8)
$prompts = [regex]::Matches($main, '(?m)^\s*prompt = "([^"]+)"')
if ($prompts.Count -ne 10) {
    throw "Expected 10 owner product prompts, found $($prompts.Count)."
}
foreach ($prompt in $prompts) {
    if (-not $prompt.Groups[1].Value.Contains('？') -and
        -not $prompt.Groups[1].Value.Contains('か？')) {
        throw "Owner question not Japanese: $($prompt.Groups[1].Value)"
    }
}
if (-not $main.Contains('Read-Host "$Prompt [y=問題なし / n=問題あり / s=未確認]"') -or
    -not $main.Contains('return "pending"') -or
    -not $main.Contains('if ($combinedDecision -eq "fail")') -or
    $main.Contains('Core acceptance did not produce summary.json.')) {
    # The last item is a translation check, not a change to gate behavior.
    throw 'Product review input/decision or Japanese instructions regressed.'
}
if ($main.Contains('Does the app clearly look') -or
    $main.Contains('Please answer y or n.')) {
    throw 'An English owner prompt remains in the product review.'
}

. (Join-Path $PSScriptRoot 'ViewerScrollReviewChecks.ps1')
$checks = @(New-ViewerScrollReviewChecks)
$expectedIds = @(
    'gridSmallForward',
    'gridBurstReverse',
    'listSmallForward',
    'listBurstReverse',
    'jumpRecovery'
)
if ($checks.Count -ne 5 -or
    @($checks | Where-Object { $_.status -ne 'pending' }).Count -ne 0 -or
    @(Compare-Object -ReferenceObject $expectedIds -DifferenceObject @($checks | ForEach-Object { $_.id }) -SyncWindow 5).Count -ne 0) {
    throw 'The five canonical scroll review IDs or pending defaults changed.'
}
foreach ($check in $checks) {
    if (-not $check.title.Contains('：') -and
        -not $check.title.Contains('移動')) {
        throw "Untranslated scroll title: $($check.id)"
    }
    if (-not $check.instruction.Contains('確認')) {
        throw "Untranslated scroll procedure: $($check.id)"
    }
}

Write-Host '日本語の確認項目10件・スクロール5件・UTF-8 BOM・未確認判定: PASS'
