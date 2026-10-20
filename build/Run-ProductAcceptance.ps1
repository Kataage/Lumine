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
    throw "The consolidated physical scroll review requires -InteractiveReview."
}
if ($IncludeViewerScrollReview -and $ThumbnailStorageMode -ne "MemoryOnly") {
    throw "Issue #634 final scroll review requires MemoryOnly thumbnail storage."
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "Lumine product acceptance requires Windows."
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw "Lumine product acceptance requires Windows x64."
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
    throw "Missing core acceptance wrapper: $coreWrapper"
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
    Write-Host "Using existing automated acceptance results from $outputRoot"
}

if (-not (Test-Path -LiteralPath $coreSummaryPath)) {
    throw "Core acceptance did not produce summary.json."
}

$coreSummary =
    Get-Content -LiteralPath $coreSummaryPath -Raw |
        ConvertFrom-Json

if ($coreSummary.automatedDecision -ne "pass") {
    throw "Core acceptance summary did not report pass."
}

if ($coreSummary.productAutomated.result -ne "pass") {
    throw "NativeAOT product workflow acceptance did not report pass."
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
        throw "Product automated gate '$gateName' did not report pass."
    }
}

$viewerScrollChecks = @()
$viewerScrollReportPath = Join-Path $outputRoot "viewer-scroll-owner-report.json"
if ($IncludeViewerScrollReview) {
    $sharedChecksPath = Join-Path $PSScriptRoot "ViewerScrollReviewChecks.ps1"
    if (-not (Test-Path -LiteralPath $sharedChecksPath -PathType Leaf)) {
        throw "Missing shared physical scroll checklist: $sharedChecksPath"
    }
    . $sharedChecksPath
    $viewerScrollChecks = @(New-ViewerScrollReviewChecks)
    if ($viewerScrollChecks.Count -ne 5 -or
        @($viewerScrollChecks | Where-Object { $_.status -ne "pending" }).Count -ne 0) {
        throw "Consolidated Viewer scroll checklist was incomplete or already answered."
    }
    # Never let an earlier review report be mistaken for current approval.
    if (Test-Path -LiteralPath $viewerScrollReportPath) {
        Remove-Item -LiteralPath $viewerScrollReportPath -Force
    }
}

$manualChecks = @(
    [ordered]@{
        id = "identity"
        title = "Lumine identity"
        prompt = "Does the app clearly look and feel like Lumine, with coherent dark branding and no generic Core/test-shell surface in normal use?"
        status = "pending"
    },
    [ordered]@{
        id = "navigation"
        title = "Shell and navigation"
        prompt = "Do global navigation, contextual Library/Folder/Tag/Publication panes and the image canvas feel like one coherent hierarchy, including overlay behavior at compact width?"
        status = "pending"
    },
    [ordered]@{
        id = "browse"
        title = "Browse usability"
        prompt = "Are Grid/List, density, search/sort/filter, tag assignment and fast direction-reversing scroll responsive without disruptive blanking or excessive command chrome?"
        status = "pending"
    },
    [ordered]@{
        id = "organization"
        title = "Organization workflow"
        prompt = "Do single/Ctrl/Shift selection, Inspector metadata editing, bulk actions and destructive confirmations feel clear and dependable?"
        status = "pending"
    },
    [ordered]@{
        id = "viewer"
        title = "Viewer discoverability and comfort"
        prompt = "Can a first-time user discover how to open an image, and are previous/next, Fit, zoom, 1:1, Info, fullscreen, pan and close/back clear without relying on hidden shortcuts?"
        status = "pending"
    },
    [ordered]@{
        id = "productStates"
        title = "Product states and feedback"
        prompt = "Do Welcome, Loading, Empty, No Match and recoverable Error each show one clear message and next action, with success feedback transient and technical detail secondary?"
        status = "pending"
    },
    [ordered]@{
        id = "creativeArchive"
        title = "Creative archive"
        prompt = "Are Work, Generation Group, directed Relation and Publication understandable and usable without AI?"
        status = "pending"
    },
    [ordered]@{
        id = "settingsPortable"
        title = "Settings and storage"
        prompt = "Are common Settings easy to find, advanced cache/storage/diagnostics progressively disclosed, and portable/storage behavior free of surprising side effects?"
        status = "pending"
    },
    [ordered]@{
        id = "accessibilityResponsive"
        title = "Accessibility and responsive polish"
        prompt = "Does keyboard-only navigation follow the visual order; are 900x600, 1024x768, 1440x900 and 1920x1080 usable; and are 100%, 125%, 150%, 200% and 225% Windows text scales clear without clipping or color-only selection/destructive cues?"
        status = "pending"
    },
    [ordered]@{
        id = "productVerdict"
        title = "Daily-use product verdict"
        prompt = "Did you observe no unresolved P0/P1 issue, and does normal daily use feel like Lumine rather than a technical prototype?"
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
    $lines.Add("# Lumine v2 Product Acceptance manual checklist")
    $lines.Add("")
    $lines.Add("Automated NativeAOT acceptance: PASS")
    $lines.Add("Revision: $($coreSummary.appRevision)")
    $lines.Add("Assets: $($coreSummary.assetCount)")
    $lines.Add("Library path is intentionally not written; summary uses the existing path hash.")
    $lines.Add("")
    $lines.Add("Use the same representative library and the same acceptance data root. Do not judge from CI alone.")
    $lines.Add("")

    foreach ($check in $Checks) {
        $marker = " "
        if ($check.status -eq "pass") {
            $marker = "x"
        }

        $lines.Add(
            "- [$marker] $($check.title): $($check.prompt) (status: $($check.status))")
    }

    $lines.Add("")
    $lines.Add("If any item fails, keep #443 / #410 / #397 open as applicable and record a focused follow-up issue before AI work starts.")

    $lines |
        Set-Content -LiteralPath $manualChecklistPath -Encoding UTF8
}

function Ask-ScrollPassFailSkip {
    param([Parameter(Mandatory = $true)][string]$Prompt)

    while ($true) {
        $answer = (Read-Host "$Prompt [y/n/s=skip]").Trim().ToLowerInvariant()
        if ($answer -in @("y", "yes")) { return "pass" }
        if ($answer -in @("n", "no")) { return "fail" }
        if ($answer -in @("s", "skip", "")) { return "pending" }
        Write-Host "Choose y, n or s."
    }
}

function Ask-PassFail {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt
    )

    while ($true) {
        $answer =
            (Read-Host "$Prompt [y/n]").Trim()

        if ($answer -match '^(y|yes)$') {
            return "pass"
        }

        if ($answer -match '^(n|no)$') {
            return "fail"
        }

        Write-Host "Please answer y or n."
    }
}

if ($InteractiveReview) {
    Write-Host ""
    Write-Host "=== Lumine manual product review ==="
    Write-Host "The automated NativeAOT gate passed."
    Write-Host "Lumine will now open normally using the acceptance data root."
    Write-Host "Select the representative library already registered in the Library navigation, exercise the checklist, then close Lumine."
    if ($IncludeViewerScrollReview) {
        Write-Host "This one app session also covers all five physical Grid/List scroll checks for Issue #634:"
        foreach ($check in $viewerScrollChecks) {
            Write-Host " - $($check.title): $($check.instruction)"
        }
        Write-Host "Use the actual Windows display/GPU and representative mixed-format images."
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
        throw "Unable to start Lumine for manual product review."
    }

    try {
        $process.WaitForExit()

        if ($process.ExitCode -ne 0) {
            throw "Manual review Lumine process exited with code $($process.ExitCode)."
        }
    }
    finally {
        $process.Dispose()
    }

    Write-Host ""
    Write-Host "Record the real-user observations:"

    foreach ($check in $manualChecks) {
        $check.status =
            Ask-PassFail -Prompt $check.prompt
    }
    if ($IncludeViewerScrollReview) {
        Write-Host "Record the five Issue #634 physical scroll observations from the SAME app session:"
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
    $viewerFailed = @($viewerScrollChecks | Where-Object { $_.status -eq "fail" }).Count
    $viewerPending = @($viewerScrollChecks | Where-Object { $_.status -eq "pending" }).Count
    if ($viewerFailed -gt 0) { $viewerScrollManualDecision = "fail" }
    elseif ($viewerPending -gt 0) { $viewerScrollManualDecision = "pending" }
    elseif ($softwareRenderingOverride) { $viewerScrollManualDecision = "not-qualified-software-renderer" }
    else { $viewerScrollManualDecision = "owner-reported-pass" }
}

$combinedDecision =
    "automated-pass-manual-review-required"
if ($manualDecision -eq "pass") {
    $combinedDecision = "pass"
}
elseif ($manualDecision -eq "fail") {
    $combinedDecision = "fail"
}
if ($IncludeViewerScrollReview) {
    if ($viewerScrollManualDecision -eq "fail") {
        $combinedDecision = "fail"
    }
    elseif ($viewerScrollManualDecision -ne "owner-reported-pass" -and
            $combinedDecision -eq "pass") {
        $combinedDecision = "automated-pass-manual-review-required"
    }
}

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
        Write-Warning "GPU inventory unavailable; human review status is unchanged."
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
Write-Host "Product automated acceptance: PASS"
Write-Host "Manual review            : $manualDecision"
if ($IncludeViewerScrollReview) {
    Write-Host "Viewer physical review  : $viewerScrollManualDecision"
    Write-Host "Viewer review report    : $viewerScrollReportPath"
}
Write-Host "Combined decision        : $combinedDecision"
Write-Host "Product summary          : $productSummaryPath"
Write-Host "Manual checklist         : $manualChecklistPath"

if ($combinedDecision -eq "fail") {
    throw "Consolidated Lumine product or scroll review reported a failure. Keep #635/#634 open."
}
