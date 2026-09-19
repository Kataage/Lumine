param(
    [Parameter(Mandatory = $true)]
    [string]$FixtureDir,

    [Parameter(Mandatory = $true)]
    [string]$HardwareId,

    [string]$Cpu = "",
    [Int64]$RamBytes = 0,
    [string]$ResultsDir = "",
    [string]$CaseTimeout = "15m",
    [switch]$Setup
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Set-Location $RepoRoot

if (-not (Test-Path $FixtureDir -PathType Container)) {
    throw "FixtureDir does not exist: $FixtureDir"
}
$FixtureDir = (Resolve-Path $FixtureDir).Path

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

Require-Command "go"
Require-Command "git"

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

if ([string]::IsNullOrWhiteSpace($Cpu)) {
    $Cpu = ((Get-CimInstance Win32_Processor | ForEach-Object { $_.Name.Trim() }) -join " + ")
}
if ($RamBytes -le 0) {
    $RamBytes = [Int64](Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
}
$LumineVersion = (& git rev-parse HEAD).Trim()
if ([string]::IsNullOrWhiteSpace($LumineVersion)) {
    throw "Could not resolve Lumine git commit."
}

$Catalog = Join-Path $RepoRoot "benchmarks\ai\catalogs\lightweight-vision-v1.json"
$Requirements = Join-Path $RepoRoot "benchmarks\ai\adapters\requirements-lightvision.txt"
$Venv = Join-Path $RepoRoot ".venv-lightvision-bench"
$Python = Join-Path $Venv "Scripts\python.exe"

function Ensure-Venv {
    $created = $false
    if (-not (Test-Path $Python -PathType Leaf)) {
        Write-Host "[Lightweight Vision] creating virtual environment..."
        & $PythonBootstrap @PythonBootstrapArgs -m venv $Venv
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create Lightweight Vision virtual environment."
        }
        $created = $true
    }
    if ($created -or $Setup) {
        Write-Host "[Lightweight Vision] installing pinned dependencies..."
        & $Python -m pip install --disable-pip-version-check -r $Requirements
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to install Lightweight Vision dependencies."
        }
    }
}

function Invoke-AIBench([string[]]$Arguments) {
    & go run ./cmd/ai-bench @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "ai-bench failed: $($Arguments -join ' ')"
    }
}

Write-Host "Validating Lightweight Vision fixture pack..."
Invoke-AIBench @(
    "validate-catalog",
    "-catalog", $Catalog,
    "-fixtures-dir", $FixtureDir
)

Ensure-Venv

$candidates = @(
    [PSCustomObject]@{
        Name = "florence-2-base"
        Profile = "benchmarks\ai\profiles\florence-2-base.json"
        Adapter = "benchmarks\ai\adapters\lightvision_florence.py"
        Result = "florence-2-base.json"
    },
    [PSCustomObject]@{
        Name = "smolvlm-500m-q8"
        Profile = "benchmarks\ai\profiles\smolvlm-500m-q8.json"
        Adapter = "benchmarks\ai\adapters\lightvision_smolvlm.py"
        Result = "smolvlm-500m-q8.json"
    }
)

Write-Host ""
Write-Host "Benchmark environment"
Write-Host "  Hardware ID : $HardwareId"
Write-Host "  CPU         : $Cpu"
Write-Host "  RAM bytes   : $RamBytes"
Write-Host "  Lumine      : $LumineVersion"
Write-Host "  Fixtures    : $FixtureDir"
Write-Host "  Results     : $ResultsDir"
Write-Host ""

$resultPaths = @()
foreach ($candidate in $candidates) {
    $profilePath = Join-Path $RepoRoot $candidate.Profile
    $adapterPath = Join-Path $RepoRoot $candidate.Adapter
    $resultPath = Join-Path $ResultsDir $candidate.Result

    Write-Host "[$($candidate.Name)] running controlled CPU benchmark..."
    Invoke-AIBench @(
        "run",
        "-catalog", $Catalog,
        "-profile", $profilePath,
        "-adapter", $Python,
        "-adapter-arg", $adapterPath,
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
    $resultPaths += $resultPath
}

$reporter = Join-Path $RepoRoot "benchmarks\ai\adapters\lightvision_report.py"
$reportPath = Join-Path $ResultsDir "lightvision-comparison.md"
Write-Host "Rendering Lightweight Vision comparison report..."
$reportOutput = & $Python $reporter @resultPaths
if ($LASTEXITCODE -ne 0) {
    throw "Failed to render Lightweight Vision comparison report."
}
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
    candidates = @($candidates | ForEach-Object { $_.Name })
    resultFiles = @($resultPaths | ForEach-Object { Split-Path $_ -Leaf })
    comparisonReport = (Split-Path $reportPath -Leaf)
}
$runInfoPath = Join-Path $ResultsDir "lightvision-run-info.json"
$runInfo | ConvertTo-Json -Depth 5 | Set-Content -Path $runInfoPath -Encoding UTF8

Write-Host ""
Write-Host "Lightweight Vision benchmark complete."
Write-Host "Comparison: $reportPath"
Write-Host "Run info  : $runInfoPath"
