param(
    [string]$FixtureDir = "",
    [string]$HardwareId = "",

    [string]$Cpu = "",
    [Int64]$RamBytes = 0,
    [string]$ResultsDir = "",
    [string]$CaseTimeout = "20m",
    [switch]$Setup,
    [switch]$InspectHereticOnly,
    [switch]$ApplyHereticPin,
    [switch]$AllowLlamaServerOverride
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Set-Location $RepoRoot

if ($ApplyHereticPin -and -not $InspectHereticOnly) {
    throw "-ApplyHereticPin is only valid together with -InspectHereticOnly."
}

if ([string]::IsNullOrWhiteSpace($ResultsDir)) {
    $ResultsDir = Join-Path $RepoRoot "benchmarks\ai\results\local"
}
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
$ResultsDir = (Resolve-Path $ResultsDir).Path

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command is not available: $Name"
    }
}

$PythonBootstrap = $null
$PythonBootstrapArgs = @()
if (Get-Command "py" -ErrorAction SilentlyContinue) {
    $PythonBootstrap = "py"
    $PythonBootstrapArgs = @("-3")
} elseif (Get-Command "python" -ErrorAction SilentlyContinue) {
    $PythonBootstrap = "python"
} else {
    throw "Python 3 is required. Install Python so either 'py' or 'python' is available."
}

$Catalog = Join-Path $RepoRoot "benchmarks\ai\catalogs\advanced-vision-v1.json"
$Requirements = Join-Path $RepoRoot "benchmarks\ai\adapters\requirements-advancedvision.txt"
$Venv = Join-Path $RepoRoot ".venv-advancedvision-bench"
$Python = Join-Path $Venv "Scripts\python.exe"
$Adapter = Join-Path $RepoRoot "benchmarks\ai\adapters\advancedvision_llamacpp.py"
$Reporter = Join-Path $RepoRoot "benchmarks\ai\adapters\advancedvision_report.py"
$PinInspector = Join-Path $RepoRoot "benchmarks\ai\adapters\advancedvision_pin_inspect.py"
$HereticProfileRelative = "benchmarks\ai\profiles\advanced-qwen3-vl-2b-heretic-q4.json"
$HereticProfile = Join-Path $RepoRoot $HereticProfileRelative
$HereticPinInfoPath = Join-Path $ResultsDir "advanced-heretic-pin-info.json"

function Ensure-Venv {
    $created = $false
    if (-not (Test-Path $Python -PathType Leaf)) {
        Write-Host "[Advanced Vision] creating virtual environment..."
        & $PythonBootstrap @PythonBootstrapArgs -m venv $Venv
        if ($LASTEXITCODE -ne 0) { throw "Failed to create Advanced Vision virtual environment." }
        $created = $true
    }
    if ($created -or $Setup) {
        Write-Host "[Advanced Vision] installing pinned dependencies..."
        & $Python -m pip install --disable-pip-version-check -r $Requirements
        if ($LASTEXITCODE -ne 0) { throw "Failed to install Advanced Vision dependencies." }
    }
}

function Invoke-AIBench([string[]]$Arguments) {
    & go run ./cmd/ai-bench @Arguments | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "ai-bench failed: $($Arguments -join ' ')"
    }
}

function Test-ProfilePinned([string]$ProfilePath) {
    $profile = Get-Content $ProfilePath -Raw | ConvertFrom-Json
    $version = ([string]$profile.version).Trim()
    $artifactHash = ([string]$profile.artifactSha256).Trim()
    $modelFile = ([string]$profile.parameters.modelFile).Trim()
    $modelHash = ([string]$profile.parameters.modelSha256).Trim()
    $modelSize = [Int64]$profile.parameters.modelSizeBytes
    $mmprojFile = ([string]$profile.parameters.mmprojFile).Trim()
    $mmprojHash = ([string]$profile.parameters.mmprojSha256).Trim()
    $mmprojSize = [Int64]$profile.parameters.mmprojSizeBytes
    $runtimeRelease = ([string]$profile.parameters.llamaRelease).Trim()
    $runtimeHash = ([string]$profile.parameters.llamaWindowsCpuArchiveSha256).Trim()

    return (
        $version.Length -eq 40 -and
        $artifactHash.Length -eq 64 -and
        [Int64]$profile.modelSizeBytes -eq ($modelSize + $mmprojSize) -and
        $modelSize -gt 0 -and
        $mmprojSize -gt 0 -and
        -not [string]::IsNullOrWhiteSpace($modelFile) -and
        $modelHash.Length -eq 64 -and
        -not [string]::IsNullOrWhiteSpace($mmprojFile) -and
        $mmprojHash.Length -eq 64 -and
        -not [string]::IsNullOrWhiteSpace($runtimeRelease) -and
        $runtimeHash.Length -eq 64
    )
}

Ensure-Venv

if ($InspectHereticOnly) {
    $inspectArgs = @($PinInspector, "--out", $HereticPinInfoPath)
    if ($ApplyHereticPin) {
        $inspectArgs += "--apply"
    }
    $inspectArgs += $HereticProfile

    Write-Host "Resolving immutable Advanced Vision Heretic metadata..."
    & $Python @inspectArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Advanced Vision Heretic pin inspection failed."
    }

    if ($ApplyHereticPin -and -not (Test-ProfilePinned $HereticProfile)) {
        throw "Applied Heretic profile is still not fully pinned."
    }

    Write-Host ""
    Write-Host "Advanced Vision Heretic pin inspection complete."
    Write-Host "Pin info: $HereticPinInfoPath"
    if ($ApplyHereticPin) {
        Write-Host "Heretic profile was explicitly updated and revalidated as immutable."
    } else {
        Write-Host "Profile was not modified. Re-run with -InspectHereticOnly -ApplyHereticPin to apply the resolved pin explicitly."
    }
    return
}

if ([string]::IsNullOrWhiteSpace($FixtureDir) -or -not (Test-Path $FixtureDir -PathType Container)) {
    throw "-FixtureDir must point to the private Advanced Vision fixture directory."
}
$FixtureDir = (Resolve-Path $FixtureDir).Path

if ([string]::IsNullOrWhiteSpace($HardwareId)) {
    throw "-HardwareId is required for controlled Advanced Vision evidence."
}

if (-not $AllowLlamaServerOverride -and -not [string]::IsNullOrWhiteSpace($env:LUMINE_LLAMA_SERVER)) {
    throw "LUMINE_LLAMA_SERVER is set. Controlled Advanced Vision evidence must use the pinned runtime. Clear it or pass -AllowLlamaServerOverride for debugging only."
}

Require-Command "go"
Require-Command "git"

if ([string]::IsNullOrWhiteSpace($Cpu)) {
    $Cpu = ((Get-CimInstance Win32_Processor | ForEach-Object { $_.Name.Trim() }) -join " + ")
}
if ($RamBytes -le 0) {
    $RamBytes = [Int64](Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
}
$LumineVersion = (& git rev-parse HEAD).Trim()

Write-Host "Validating Advanced Vision fixture pack..."
Invoke-AIBench @(
    "validate-catalog",
    "-catalog", $Catalog,
    "-fixtures-dir", $FixtureDir
)

$candidates = @(
    [PSCustomObject]@{ Name="qwen3-vl-2b"; Profile="benchmarks\ai\profiles\advanced-qwen3-vl-2b-q4.json"; Result="advanced-qwen3-vl-2b-q4.json" },
    [PSCustomObject]@{ Name="qwen3-vl-2b-abliterated"; Profile="benchmarks\ai\profiles\advanced-qwen3-vl-2b-abliterated-q4.json"; Result="advanced-qwen3-vl-2b-abliterated-q4.json" },
    [PSCustomObject]@{ Name="internvl3.5-2b"; Profile="benchmarks\ai\profiles\advanced-internvl3.5-2b-q4.json"; Result="advanced-internvl3.5-2b-q4.json" },
    [PSCustomObject]@{ Name="smolvlm2-2.2b"; Profile="benchmarks\ai\profiles\advanced-smolvlm2-2.2b-q4.json"; Result="advanced-smolvlm2-2.2b-q4.json" },
    [PSCustomObject]@{ Name="minicpm-v4.6"; Profile="benchmarks\ai\profiles\advanced-minicpm-v4.6-q4.json"; Result="advanced-minicpm-v4.6-q4.json" },
    [PSCustomObject]@{ Name="granite-vision-4.1-4b"; Profile="benchmarks\ai\profiles\advanced-granite-vision-4.1-4b-q4.json"; Result="advanced-granite-vision-4.1-4b-q4.json" }
)

$hereticPinned = Test-ProfilePinned $HereticProfile
if ($hereticPinned) {
    $candidates += [PSCustomObject]@{
        Name="qwen3-vl-2b-heretic"
        Profile=$HereticProfileRelative
        Result="advanced-qwen3-vl-2b-heretic-q4.json"
    }
}

Write-Host ""
Write-Host "Benchmark environment"
Write-Host "  Hardware ID : $HardwareId"
Write-Host "  CPU         : $Cpu"
Write-Host "  RAM bytes   : $RamBytes"
Write-Host "  Lumine      : $LumineVersion"
Write-Host "  Fixtures    : $FixtureDir"
Write-Host "  Results     : $ResultsDir"
Write-Host ""

function Run-Candidate(
    [string]$Name,
    [string]$ProfileRelative,
    [string]$ResultName
) {
    $profilePath = Join-Path $RepoRoot $ProfileRelative
    $resultPath = Join-Path $ResultsDir $ResultName

    Write-Host "[$Name] running controlled CPU benchmark..."
    Invoke-AIBench @(
        "run",
        "-catalog", $Catalog,
        "-profile", $profilePath,
        "-adapter", $Python,
        "-adapter-arg", $Adapter,
        "-fixtures-dir", $FixtureDir,
        "-hardware-id", $HardwareId,
        "-cpu", $Cpu,
        "-ram-bytes", $RamBytes.ToString(),
        "-lumine-version", $LumineVersion,
        "-case-timeout", $CaseTimeout,
        "-out", $resultPath
    )
    Invoke-AIBench @(
        "validate-result",
        "-catalog", $Catalog,
        "-result", $resultPath
    )
    return $resultPath
}

$resultPaths = @()
foreach ($candidate in $candidates) {
    $resultPaths += Run-Candidate $candidate.Name $candidate.Profile $candidate.Result
}

if (-not $hereticPinned) {
    throw "Heretic profile is not fully pinned. Run -InspectHereticOnly, review the metadata, and apply/commit the immutable pin before controlled evidence."
}

$reportPath = Join-Path $ResultsDir "advanced-vision-comparison.md"
$reportOutput = & $Python $Reporter @resultPaths
if ($LASTEXITCODE -ne 0) { throw "Failed to render Advanced Vision comparison report." }
$reportOutput | Set-Content -Path $reportPath -Encoding UTF8

$runInfo = [ordered]@{
    schemaVersion = 1
    generatedAt = [DateTime]::UtcNow.ToString("o")
    hardwareId = $HardwareId
    cpu = $Cpu
    ramBytes = $RamBytes
    lumineVersion = $LumineVersion
    fixtureDir = $FixtureDir
    caseTimeout = $CaseTimeout
    hereticPinned = [bool]$hereticPinned
    candidates = @($candidates | ForEach-Object { $_.Name })
    resultFiles = @($resultPaths | ForEach-Object { Split-Path $_ -Leaf })
    comparisonReport = (Split-Path $reportPath -Leaf)
}
$runInfoPath = Join-Path $ResultsDir "advanced-vision-run-info.json"
$runInfo | ConvertTo-Json -Depth 5 | Set-Content -Path $runInfoPath -Encoding UTF8

Write-Host ""
Write-Host "Advanced Vision benchmark complete."
Write-Host "Comparison: $reportPath"
Write-Host "Run info  : $runInfoPath"
