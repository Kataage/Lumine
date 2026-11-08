param(
    [Parameter(Mandatory = $true)]
    [string]$Exe,

    [Parameter(Mandatory = $true)]
    [string]$Library,

    [string]$OutputDirectory =
        (Join-Path $PWD "artifacts/product-acceptance"),

    [int]$MinimumAssets = 1000,

    [int]$BrowseSeconds = 60,

    [int]$IdleSeconds = 10,

    [double]$MaxFastScrollMs = 1500,

    [string]$HardwareId = $env:COMPUTERNAME,

    [string]$Revision = "",

    [ValidateSet("PersistentDisk", "MemoryOnly")]
    [string]$ThumbnailStorageMode = "MemoryOnly",

    [switch]$UseExistingAutomatedResults,

    [switch]$InteractiveReview,

    # Issue #635 final one-session handoff: five independent Issue #634
    # physical wheel checks inside the same already-open product review.
    [switch]$IncludeViewerScrollReview
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($IncludeViewerScrollReview -and -not $InteractiveReview) {
    throw "スクロール確認には -InteractiveReview を指定してください。"
}
if ($IncludeViewerScrollReview -and $ThumbnailStorageMode -ne "MemoryOnly") {
    throw "スクロール確認にはサムネイル保存方式 MemoryOnly が必要です。"
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "Lumineの製品検証はWindowsで実行してください。"
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw "Lumineの製品検証には64ビットWindowsが必要です。"
}

$exePath = (Resolve-Path -LiteralPath $Exe).Path
$libraryPath = (Resolve-Path -LiteralPath $Library).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$coreWrapper =
    Join-Path $PSScriptRoot "Run-RealLibraryAcceptance.ps1"
$coreSummaryPath =
    Join-Path $outputRoot "summary.json"
$productSummaryPath =
    Join-Path $outputRoot "product-summary.json"
$manualChecklistPath =
    Join-Path $outputRoot "MANUAL-CHECKLIST.md"
$dataRoot =
    Join-Path $outputRoot "data"

if (-not (Test-Path -LiteralPath $coreWrapper)) {
    throw "自動検証スクリプトが見つかりません: $coreWrapper"
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

foreach ($stale in @(
    $productSummaryPath,
    $manualChecklistPath
)) {
    if (Test-Path -LiteralPath $stale) {
        Remove-Item -LiteralPath $stale -Force
    }
}

$coreArguments = @{
    Exe = $exePath
    Library = $libraryPath
    OutputDirectory = $outputRoot
    MinimumAssets = $MinimumAssets
    BrowseSeconds = $BrowseSeconds
    IdleSeconds = $IdleSeconds
    MaxFastScrollMs = $MaxFastScrollMs
    HardwareId = $HardwareId
    Revision = $Revision
    ThumbnailStorageMode = $ThumbnailStorageMode
}

if (-not $UseExistingAutomatedResults) {
    & $coreWrapper @coreArguments
}
else {
    Write-Host "既存の自動検証結果を再利用します: $outputRoot"
}

if (-not (Test-Path -LiteralPath $coreSummaryPath)) {
    throw "自動検証のsummary.jsonが見つかりません。"
}

$coreSummary =
    Get-Content -LiteralPath $coreSummaryPath -Raw |
        ConvertFrom-Json

if ($coreSummary.automatedDecision -ne "pass") {
    throw "自動検証が合格していません。手動確認を合格扱いにはできません。"
}

if ($coreSummary.productAutomated.result -ne "pass") {
    throw "NativeAOT製品機能の自動検証が合格していません。"
}

$productGateNames = @(
    "identity",
    "navigation",
    "browse",
    "organization",
    "viewer",
    "creativeArchive",
    "productStates",
    "settingsPortable",
    "searchMetadataRoundTrip"
)

foreach ($gateName in $productGateNames) {
    if ([string]$coreSummary.productAutomated.$gateName -ne "pass") {
        throw "製品機能の自動検証「$gateName」が合格していません。"
    }
}

$reviewDecisionPath = Join-Path $PSScriptRoot "OwnerReviewDecisions.ps1"
if (-not (Test-Path -LiteralPath $reviewDecisionPath -PathType Leaf)) {
    throw "手動判定スクリプトが見つかりません: $reviewDecisionPath"
}
. $reviewDecisionPath

$viewerScrollChecks = @()
$viewerScrollReportPath = Join-Path $outputRoot "viewer-scroll-owner-report.json"
if ($IncludeViewerScrollReview) {
    $sharedChecksPath = Join-Path $PSScriptRoot "ViewerScrollReviewChecks.ps1"
    if (-not (Test-Path -LiteralPath $sharedChecksPath -PathType Leaf)) {
        throw "スクロール確認の共通チェックリストが見つかりません: $sharedChecksPath"
    }
    . $sharedChecksPath
    $viewerScrollChecks = @(New-ViewerScrollReviewChecks)
    if ($viewerScrollChecks.Count -ne 5 -or
        @($viewerScrollChecks | Where-Object { $_.status -ne "pending" }).Count -ne 0) {
        throw "スクロール確認の5項目が不足しているか、既に回答されています。"
    }
    # Never let an earlier review report be mistaken for current approval.
    if (Test-Path -LiteralPath $viewerScrollReportPath) {
        Remove-Item -LiteralPath $viewerScrollReportPath -Force
    }
}

$manualChecks = @(
    [ordered]@{
        id = "identity"
        title = "Lumineのデザイン"
        prompt = "全体のダークデザインに統一感があり、開発用の仮画面ではなくLumineとして自然に使えますか？"
        status = "pending"
    },
    [ordered]@{
        id = "navigation"
        title = "メニューと画面の構成"
        prompt = "ライブラリ・フォルダ・タグ・公開履歴のメニューと画像一覧がわかりやすく、小さな画面でもメニューが自然に開閉しますか？"
        status = "pending"
    },
    [ordered]@{
        id = "browse"
        title = "画像一覧の使いやすさ"
        prompt = "グリッド・リスト・表示密度・検索・並び替え・絞り込み・タグ付けを使いやすく操作でき、スクロール時に不自然な空白が出ませんか？"
        status = "pending"
    },
    [ordered]@{
        id = "organization"
        title = "画像の整理と編集"
        prompt = "単一・Ctrl・Shiftによる選択、詳細情報の編集、一括操作、削除確認がわかりやすく安全に使えますか？"
        status = "pending"
    },
    [ordered]@{
        id = "viewer"
        title = "画像ビューアーの操作"
        prompt = "画像の開き方と、前後移動・画面に合わせる・拡大縮小・原寸・情報・全画面・移動・戻る操作が、隠れたショートカットに頼らずわかりますか？"
        status = "pending"
    },
    [ordered]@{
        id = "productStates"
        title = "案内・読み込み・エラー表示"
        prompt = "初回・読み込み中・画像なし・検索結果なし・エラー時の表示は簡潔で、次に何をすべきかわかりますか？"
        status = "pending"
    },
    [ordered]@{
        id = "creativeArchive"
        title = "作品・公開履歴の管理"
        prompt = "作品・生成グループ・画像の関連付け・公開履歴は、各項目の意味がわかり、問題なく操作できますか？"
        status = "pending"
    },
    [ordered]@{
        id = "settingsPortable"
        title = "設定と保存先"
        prompt = "よく使う設定が見つかりやすく、詳細設定は必要なときだけ表示され、保存先やポータブル動作に問題がありませんか？"
        status = "pending"
    },
    [ordered]@{
        id = "accessibilityResponsive"
        title = "キーボード操作と小画面表示"
        prompt = "キーボード操作の順序は自然で、900×600～1920×1080やWindowsの文字倍率100～225%でも文字・ボタンが欠けず、選択状態も区別できますか？"
        status = "pending"
    },
    [ordered]@{
        id = "productVerdict"
        title = "日常利用の総合評価"
        prompt = "重大な未解決問題がなく、試作品ではなく日常的に使える完成度だと判断できますか？"
        status = "pending"
    }
)

function Write-ManualChecklist {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Checks
    )

    $lines =
        New-Object System.Collections.Generic.List[string]
    $lines.Add("# Lumine v2 実機操作チェックリスト")
    $lines.Add("")
    $lines.Add("NativeAOT自動検証: 合格")
    $lines.Add("ビルド識別子: $($coreSummary.appRevision)")
    $lines.Add("画像件数: $($coreSummary.assetCount)")
    $lines.Add("画像ライブラリの実パスは記録しません。既存のパス識別ハッシュを使用します。")
    $lines.Add("")
    $lines.Add("同じ画像ライブラリと検証データを使用してください。CI結果だけで実機合格と判断しないでください。")
    $lines.Add("")

    foreach ($check in $Checks) {
        $marker = " "
        if ($check.status -eq "pass") {
            $marker = "x"
        }

        $lines.Add(
            "- [$marker] $($check.title): $($check.prompt) （判定: $($check.status)）")
    }

    if ($IncludeViewerScrollReview) {
        $lines.Add("")
        $lines.Add("## Issue #634 — 同じ起動中に確認するグリッド・リストのスクロール")
        $lines.Add("GPU名は参考情報です。実際の表示品質の証明ではありません。")
        foreach ($check in $viewerScrollChecks) {
            $marker = if ($check.status -eq "pass") { "x" } else { " " }
            $lines.Add("- [$marker] $($check.title): $($check.instruction) （判定: $($check.status)）")
        }
    }
    $lines.Add("")
    $lines.Add("一つでも不合格があれば、該当Issueを未完了として扱います。CIだけで実機合格と判断しません。")

    $lines |
        Set-Content -LiteralPath $manualChecklistPath -Encoding UTF8
}

function Ask-ScrollPassFailSkip {
    param([Parameter(Mandatory = $true)][string]$Prompt)

    while ($true) {
        $answer = (Read-Host "$Prompt [y=問題なし / n=問題あり / s=未確認]").Trim().ToLowerInvariant()
        if ($answer -in @("y", "yes")) { return "pass" }
        if ($answer -in @("n", "no")) { return "fail" }
        if ($answer -in @("s", "skip", "")) { return "pending" }
        Write-Host "y（問題なし）/ n（問題あり）/ s（未確認）から選んでください。"
    }
}

function Ask-PassFail {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt
    )

    while ($true) {
        $answer =
            (Read-Host "$Prompt [y=問題なし / n=問題あり / s=未確認]").Trim()

        if ($answer -match '^(y|yes)$') {
            return "pass"
        }

        if ($answer -match '^(n|no) "y（問題なし）/ n（問題あり）/ s（未確認）から選んでください。"
    }
}

if ($InteractiveReview) {
    Write-Host ""
    Write-Host "=== Lumine 実機での操作確認 ==="
    Write-Host "NativeAOTの自動検証は合格しています。"
    Write-Host "これからLumineを通常起動します。"
    Write-Host "ライブラリで登録済みの画像フォルダを選び、操作を確認してからLumineを閉じてください。"
    if ($IncludeViewerScrollReview) {
        Write-Host "この1回の起動で、Issue #634のスクロール5項目も確認してください。"
        foreach ($check in $viewerScrollChecks) {
            Write-Host " - $($check.title): $($check.instruction)"
        }
        Write-Host "実際のWindows画面で、複数形式を含む画像ライブラリを使用してください。"
    }

    $startInfo =
        New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $exePath
    $startInfo.WorkingDirectory =
        Split-Path -Parent $exePath
    $startInfo.UseShellExecute = $false
    $startInfo.EnvironmentVariables["LUMINE_DATA_DIR"] =
        $dataRoot
    $startInfo.EnvironmentVariables["LUMINE_THUMBNAIL_STORAGE_MODE"] =
        $ThumbnailStorageMode

    $process =
        [System.Diagnostics.Process]::Start($startInfo)

    if ($null -eq $process) {
        throw "Lumineを起動できませんでした。手動検証は未完了です。"
    }

    try {
        $process.WaitForExit()

        if ($process.ExitCode -ne 0) {
            throw "Lumineが異常終了しました（終了コード: $($process.ExitCode)）。"
        }
    }
    finally {
        $process.Dispose()
    }

    Write-Host ""
    Write-Host "実際に操作した結果を入力してください。未確認は s を選べます。"

    foreach ($check in $manualChecks) {
        $check.status =
            Ask-PassFail -Prompt $check.prompt
    }
    if ($IncludeViewerScrollReview) {
        Write-Host "同じ起動中に確認したスクロール5項目の結果を入力してください。"
        foreach ($check in $viewerScrollChecks) {
            $check.status = Ask-ScrollPassFailSkip -Prompt $check.title
        }
    }
}

$failedManualChecks =
    @($manualChecks |
        Where-Object { $_.status -eq "fail" })
$pendingManualChecks =
    @($manualChecks |
        Where-Object { $_.status -eq "pending" })

$manualDecision = "pass"
if ($failedManualChecks.Count -gt 0) {
    $manualDecision = "fail"
}
elseif ($pendingManualChecks.Count -gt 0) {
    $manualDecision = "pending"
}

$viewerScrollManualDecision = "not-requested"
$softwareRenderingOverride = [string]::Equals(
    [Environment]::GetEnvironmentVariable("LUMINE_WIN32_RENDERING_MODE"),
    "Software", [StringComparison]::OrdinalIgnoreCase)
if ($IncludeViewerScrollReview) {
    $viewerScrollManualDecision = Get-ViewerScrollOwnerDecision `
        -Checks $viewerScrollChecks `
        -SoftwareRendererOverride $softwareRenderingOverride
}
$combinedDecision = Get-CombinedOwnerReviewDecision `
    -ProductManualDecision $manualDecision `
    -IncludeViewerScrollReview ([bool]$IncludeViewerScrollReview) `
    -ViewerScrollManualDecision $viewerScrollManualDecision

$coreSummarySha256 =
    ((Get-FileHash -LiteralPath $coreSummaryPath -Algorithm SHA256).Hash).ToLowerInvariant()

$unresolvedP0P1 = $null
if ($combinedDecision -eq "pass") {
    $unresolvedP0P1 = $false
}
elseif ($combinedDecision -eq "fail") {
    $unresolvedP0P1 = $true
}

$productSummary = [ordered]@{
    schemaVersion = 1
    generatedAtUtc =
        [DateTimeOffset]::UtcNow.ToString("O")
    appRevision = $coreSummary.appRevision
    hardwareId = $coreSummary.hardwareId
    assetCount = [int64]$coreSummary.assetCount
    libraryPathSha256 =
        $coreSummary.libraryPathSha256
    thumbnailStorageMode =
        $coreSummary.thumbnailStorageMode
    coreSummarySha256 =
        $coreSummarySha256
    automatedDecision = "pass"
    productAutomated =
        $coreSummary.productAutomated
    manualDecision = $manualDecision
    combinedDecision = $combinedDecision
    manualChecks = $manualChecks
    consolidatedViewerScrollReview = [bool]$IncludeViewerScrollReview
    viewerScrollManualDecision = $viewerScrollManualDecision
    viewerScrollChecks = $viewerScrollChecks
    unresolvedP0P1 =
        $unresolvedP0P1
    sourcePolicy = [ordered]@{
        representativeLibraryMutatedByAutomatedRun = $false
        productMutationScenario =
            "isolated-acceptance-scratch-library"
    }
    coreSummary =
        $coreSummaryPath
}

$productSummary |
    ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $productSummaryPath -Encoding UTF8

Write-ManualChecklist -Checks $manualChecks
if ($IncludeViewerScrollReview) {
    # Independent Issue #634 schema and decision. The GPU inventory is
    # informational, never proof of physical frame-present timing.
    $gpuNames = @()
    try {
        $gpuNames = @(Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) } |
            ForEach-Object { [string]$_.Name } | Sort-Object -Unique)
    }
    catch {
        Write-Warning "GPU情報を取得できませんでした。手動評価の結果には影響しません。"
    }
    $viewerReport = [ordered]@{
        schemaVersion = 1
        issue = 634
        evidenceClass = "human-self-report-not-gpu-present-timing"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        appRevision = $coreSummary.appRevision
        preparationOnly = $false
        osVersion = [Environment]::OSVersion.VersionString
        videoControllerNames = @($gpuNames)
        softwareRendererOverride = $softwareRenderingOverride
        thumbnailStorageMode = $ThumbnailStorageMode
        appExitCode = 0
        manualDecision = $viewerScrollManualDecision
        checks = $viewerScrollChecks
        notes = @(
            "Shared one-session Issue #635 product review; no second application launch.",
            "No physical GPU frame-present latency is measured.",
            "No private library paths, filenames, or image pixels are recorded."
        )
    }
    $viewerReport | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $viewerScrollReportPath -Encoding UTF8
}

Write-Host ""
Write-Host "製品の自動検証: 合格"
Write-Host "手動操作の確認: $manualDecision"
if ($IncludeViewerScrollReview) {
    Write-Host "スクロールの実機確認: $viewerScrollManualDecision"
    Write-Host "スクロール検証レポート: $viewerScrollReportPath"
}
Write-Host "総合判定: $combinedDecision"
Write-Host "製品検証レポート: $productSummaryPath"
Write-Host "手動チェックリスト: $manualChecklistPath"

if ($combinedDecision -eq "fail") {
    throw "製品操作またはスクロールの実機確認で不合格がありました。Issue #635/#634は未完了として扱います。"
}
) {
            return "fail"
        }

        if ($answer -match '^(s|skip)? "y（問題なし）/ n（問題あり）/ s（未確認）から選んでください。"
    }
}

if ($InteractiveReview) {
    Write-Host ""
    Write-Host "=== Lumine 実機での操作確認 ==="
    Write-Host "NativeAOTの自動検証は合格しています。"
    Write-Host "これからLumineを通常起動します。"
    Write-Host "ライブラリで登録済みの画像フォルダを選び、操作を確認してからLumineを閉じてください。"
    if ($IncludeViewerScrollReview) {
        Write-Host "この1回の起動で、Issue #634のスクロール5項目も確認してください。"
        foreach ($check in $viewerScrollChecks) {
            Write-Host " - $($check.title): $($check.instruction)"
        }
        Write-Host "実際のWindows画面で、複数形式を含む画像ライブラリを使用してください。"
    }

    $startInfo =
        New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $exePath
    $startInfo.WorkingDirectory =
        Split-Path -Parent $exePath
    $startInfo.UseShellExecute = $false
    $startInfo.EnvironmentVariables["LUMINE_DATA_DIR"] =
        $dataRoot
    $startInfo.EnvironmentVariables["LUMINE_THUMBNAIL_STORAGE_MODE"] =
        $ThumbnailStorageMode

    $process =
        [System.Diagnostics.Process]::Start($startInfo)

    if ($null -eq $process) {
        throw "Lumineを起動できませんでした。手動検証は未完了です。"
    }

    try {
        $process.WaitForExit()

        if ($process.ExitCode -ne 0) {
            throw "Lumineが異常終了しました（終了コード: $($process.ExitCode)）。"
        }
    }
    finally {
        $process.Dispose()
    }

    Write-Host ""
    Write-Host "実際に操作した結果を入力してください。未確認は s を選べます。"

    foreach ($check in $manualChecks) {
        $check.status =
            Ask-PassFail -Prompt $check.prompt
    }
    if ($IncludeViewerScrollReview) {
        Write-Host "同じ起動中に確認したスクロール5項目の結果を入力してください。"
        foreach ($check in $viewerScrollChecks) {
            $check.status = Ask-ScrollPassFailSkip -Prompt $check.title
        }
    }
}

$failedManualChecks =
    @($manualChecks |
        Where-Object { $_.status -eq "fail" })
$pendingManualChecks =
    @($manualChecks |
        Where-Object { $_.status -eq "pending" })

$manualDecision = "pass"
if ($failedManualChecks.Count -gt 0) {
    $manualDecision = "fail"
}
elseif ($pendingManualChecks.Count -gt 0) {
    $manualDecision = "pending"
}

$viewerScrollManualDecision = "not-requested"
$softwareRenderingOverride = [string]::Equals(
    [Environment]::GetEnvironmentVariable("LUMINE_WIN32_RENDERING_MODE"),
    "Software", [StringComparison]::OrdinalIgnoreCase)
if ($IncludeViewerScrollReview) {
    $viewerScrollManualDecision = Get-ViewerScrollOwnerDecision `
        -Checks $viewerScrollChecks `
        -SoftwareRendererOverride $softwareRenderingOverride
}
$combinedDecision = Get-CombinedOwnerReviewDecision `
    -ProductManualDecision $manualDecision `
    -IncludeViewerScrollReview ([bool]$IncludeViewerScrollReview) `
    -ViewerScrollManualDecision $viewerScrollManualDecision

$coreSummarySha256 =
    ((Get-FileHash -LiteralPath $coreSummaryPath -Algorithm SHA256).Hash).ToLowerInvariant()

$unresolvedP0P1 = $null
if ($combinedDecision -eq "pass") {
    $unresolvedP0P1 = $false
}
elseif ($combinedDecision -eq "fail") {
    $unresolvedP0P1 = $true
}

$productSummary = [ordered]@{
    schemaVersion = 1
    generatedAtUtc =
        [DateTimeOffset]::UtcNow.ToString("O")
    appRevision = $coreSummary.appRevision
    hardwareId = $coreSummary.hardwareId
    assetCount = [int64]$coreSummary.assetCount
    libraryPathSha256 =
        $coreSummary.libraryPathSha256
    thumbnailStorageMode =
        $coreSummary.thumbnailStorageMode
    coreSummarySha256 =
        $coreSummarySha256
    automatedDecision = "pass"
    productAutomated =
        $coreSummary.productAutomated
    manualDecision = $manualDecision
    combinedDecision = $combinedDecision
    manualChecks = $manualChecks
    consolidatedViewerScrollReview = [bool]$IncludeViewerScrollReview
    viewerScrollManualDecision = $viewerScrollManualDecision
    viewerScrollChecks = $viewerScrollChecks
    unresolvedP0P1 =
        $unresolvedP0P1
    sourcePolicy = [ordered]@{
        representativeLibraryMutatedByAutomatedRun = $false
        productMutationScenario =
            "isolated-acceptance-scratch-library"
    }
    coreSummary =
        $coreSummaryPath
}

$productSummary |
    ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $productSummaryPath -Encoding UTF8

Write-ManualChecklist -Checks $manualChecks
if ($IncludeViewerScrollReview) {
    # Independent Issue #634 schema and decision. The GPU inventory is
    # informational, never proof of physical frame-present timing.
    $gpuNames = @()
    try {
        $gpuNames = @(Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) } |
            ForEach-Object { [string]$_.Name } | Sort-Object -Unique)
    }
    catch {
        Write-Warning "GPU情報を取得できませんでした。手動評価の結果には影響しません。"
    }
    $viewerReport = [ordered]@{
        schemaVersion = 1
        issue = 634
        evidenceClass = "human-self-report-not-gpu-present-timing"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        appRevision = $coreSummary.appRevision
        preparationOnly = $false
        osVersion = [Environment]::OSVersion.VersionString
        videoControllerNames = @($gpuNames)
        softwareRendererOverride = $softwareRenderingOverride
        thumbnailStorageMode = $ThumbnailStorageMode
        appExitCode = 0
        manualDecision = $viewerScrollManualDecision
        checks = $viewerScrollChecks
        notes = @(
            "Shared one-session Issue #635 product review; no second application launch.",
            "No physical GPU frame-present latency is measured.",
            "No private library paths, filenames, or image pixels are recorded."
        )
    }
    $viewerReport | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $viewerScrollReportPath -Encoding UTF8
}

Write-Host ""
Write-Host "製品の自動検証: 合格"
Write-Host "手動操作の確認: $manualDecision"
if ($IncludeViewerScrollReview) {
    Write-Host "スクロールの実機確認: $viewerScrollManualDecision"
    Write-Host "スクロール検証レポート: $viewerScrollReportPath"
}
Write-Host "総合判定: $combinedDecision"
Write-Host "製品検証レポート: $productSummaryPath"
Write-Host "手動チェックリスト: $manualChecklistPath"

if ($combinedDecision -eq "fail") {
    throw "製品操作またはスクロールの実機確認で不合格がありました。Issue #635/#634は未完了として扱います。"
}
) {
            return "pending"
        }

        Write-Host "y（問題なし）/ n（問題あり）/ s（未確認）から選んでください。"
    }
}

if ($InteractiveReview) {
    Write-Host ""
    Write-Host "=== Lumine 実機での操作確認 ==="
    Write-Host "NativeAOTの自動検証は合格しています。"
    Write-Host "これからLumineを通常起動します。"
    Write-Host "ライブラリで登録済みの画像フォルダを選び、操作を確認してからLumineを閉じてください。"
    if ($IncludeViewerScrollReview) {
        Write-Host "この1回の起動で、Issue #634のスクロール5項目も確認してください。"
        foreach ($check in $viewerScrollChecks) {
            Write-Host " - $($check.title): $($check.instruction)"
        }
        Write-Host "実際のWindows画面で、複数形式を含む画像ライブラリを使用してください。"
    }

    $startInfo =
        New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $exePath
    $startInfo.WorkingDirectory =
        Split-Path -Parent $exePath
    $startInfo.UseShellExecute = $false
    $startInfo.EnvironmentVariables["LUMINE_DATA_DIR"] =
        $dataRoot
    $startInfo.EnvironmentVariables["LUMINE_THUMBNAIL_STORAGE_MODE"] =
        $ThumbnailStorageMode

    $process =
        [System.Diagnostics.Process]::Start($startInfo)

    if ($null -eq $process) {
        throw "Lumineを起動できませんでした。手動検証は未完了です。"
    }

    try {
        $process.WaitForExit()

        if ($process.ExitCode -ne 0) {
            throw "Lumineが異常終了しました（終了コード: $($process.ExitCode)）。"
        }
    }
    finally {
        $process.Dispose()
    }

    Write-Host ""
    Write-Host "実際に操作した結果を入力してください。未確認は s を選べます。"

    foreach ($check in $manualChecks) {
        $check.status =
            Ask-PassFail -Prompt $check.prompt
    }
    if ($IncludeViewerScrollReview) {
        Write-Host "同じ起動中に確認したスクロール5項目の結果を入力してください。"
        foreach ($check in $viewerScrollChecks) {
            $check.status = Ask-ScrollPassFailSkip -Prompt $check.title
        }
    }
}

$failedManualChecks =
    @($manualChecks |
        Where-Object { $_.status -eq "fail" })
$pendingManualChecks =
    @($manualChecks |
        Where-Object { $_.status -eq "pending" })

$manualDecision = "pass"
if ($failedManualChecks.Count -gt 0) {
    $manualDecision = "fail"
}
elseif ($pendingManualChecks.Count -gt 0) {
    $manualDecision = "pending"
}

$viewerScrollManualDecision = "not-requested"
$softwareRenderingOverride = [string]::Equals(
    [Environment]::GetEnvironmentVariable("LUMINE_WIN32_RENDERING_MODE"),
    "Software", [StringComparison]::OrdinalIgnoreCase)
if ($IncludeViewerScrollReview) {
    $viewerScrollManualDecision = Get-ViewerScrollOwnerDecision `
        -Checks $viewerScrollChecks `
        -SoftwareRendererOverride $softwareRenderingOverride
}
$combinedDecision = Get-CombinedOwnerReviewDecision `
    -ProductManualDecision $manualDecision `
    -IncludeViewerScrollReview ([bool]$IncludeViewerScrollReview) `
    -ViewerScrollManualDecision $viewerScrollManualDecision

$coreSummarySha256 =
    ((Get-FileHash -LiteralPath $coreSummaryPath -Algorithm SHA256).Hash).ToLowerInvariant()

$unresolvedP0P1 = $null
if ($combinedDecision -eq "pass") {
    $unresolvedP0P1 = $false
}
elseif ($combinedDecision -eq "fail") {
    $unresolvedP0P1 = $true
}

$productSummary = [ordered]@{
    schemaVersion = 1
    generatedAtUtc =
        [DateTimeOffset]::UtcNow.ToString("O")
    appRevision = $coreSummary.appRevision
    hardwareId = $coreSummary.hardwareId
    assetCount = [int64]$coreSummary.assetCount
    libraryPathSha256 =
        $coreSummary.libraryPathSha256
    thumbnailStorageMode =
        $coreSummary.thumbnailStorageMode
    coreSummarySha256 =
        $coreSummarySha256
    automatedDecision = "pass"
    productAutomated =
        $coreSummary.productAutomated
    manualDecision = $manualDecision
    combinedDecision = $combinedDecision
    manualChecks = $manualChecks
    consolidatedViewerScrollReview = [bool]$IncludeViewerScrollReview
    viewerScrollManualDecision = $viewerScrollManualDecision
    viewerScrollChecks = $viewerScrollChecks
    unresolvedP0P1 =
        $unresolvedP0P1
    sourcePolicy = [ordered]@{
        representativeLibraryMutatedByAutomatedRun = $false
        productMutationScenario =
            "isolated-acceptance-scratch-library"
    }
    coreSummary =
        $coreSummaryPath
}

$productSummary |
    ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $productSummaryPath -Encoding UTF8

Write-ManualChecklist -Checks $manualChecks
if ($IncludeViewerScrollReview) {
    # Independent Issue #634 schema and decision. The GPU inventory is
    # informational, never proof of physical frame-present timing.
    $gpuNames = @()
    try {
        $gpuNames = @(Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) } |
            ForEach-Object { [string]$_.Name } | Sort-Object -Unique)
    }
    catch {
        Write-Warning "GPU情報を取得できませんでした。手動評価の結果には影響しません。"
    }
    $viewerReport = [ordered]@{
        schemaVersion = 1
        issue = 634
        evidenceClass = "human-self-report-not-gpu-present-timing"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        appRevision = $coreSummary.appRevision
        preparationOnly = $false
        osVersion = [Environment]::OSVersion.VersionString
        videoControllerNames = @($gpuNames)
        softwareRendererOverride = $softwareRenderingOverride
        thumbnailStorageMode = $ThumbnailStorageMode
        appExitCode = 0
        manualDecision = $viewerScrollManualDecision
        checks = $viewerScrollChecks
        notes = @(
            "Shared one-session Issue #635 product review; no second application launch.",
            "No physical GPU frame-present latency is measured.",
            "No private library paths, filenames, or image pixels are recorded."
        )
    }
    $viewerReport | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $viewerScrollReportPath -Encoding UTF8
}

Write-Host ""
Write-Host "製品の自動検証: 合格"
Write-Host "手動操作の確認: $manualDecision"
if ($IncludeViewerScrollReview) {
    Write-Host "スクロールの実機確認: $viewerScrollManualDecision"
    Write-Host "スクロール検証レポート: $viewerScrollReportPath"
}
Write-Host "総合判定: $combinedDecision"
Write-Host "製品検証レポート: $productSummaryPath"
Write-Host "手動チェックリスト: $manualChecklistPath"

if ($combinedDecision -eq "fail") {
    throw "製品操作またはスクロールの実機確認で不合格がありました。Issue #635/#634は未完了として扱います。"
}
