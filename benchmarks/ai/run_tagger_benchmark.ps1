param(
    [Parameter(Mandatory = $true)]
    [string]$FixtureDir,

    [Parameter(Mandatory = $true)]
    [string]$HardwareId,

    [string]$Cpu = "",
    [Int64]$RamBytes = 0,
    [string]$ResultsDir = "",
    [string]$CaseTimeout = "15m",
    [switch]$Setup,
    [switch]$SkipPixAIV1
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

$Catalog = Join-Path $RepoRoot "benchmarks\ai\catalog.json"
$OnnxRequirements = Join-Path $RepoRoot "benchmarks\ai\adapters\requirements-tagger.txt"
$PixAIRequirements = Join-Path $RepoRoot "benchmarks\ai\adapters\requirements-tagger-pixai-v1.txt"
$OnnxVenv = Join-Path $RepoRoot ".venv-tagger-onnx"
$PixAIVenv = Join-Path $RepoRoot ".venv-tagger-pixai-v1"
$OnnxPython = Join-Path $OnnxVenv "Scripts\python.exe"
$PixAIPython = Join-Path $PixAIVenv "Scripts\python.exe"

function Ensure-Venv(
    [string]$VenvPath,
    [string]$PythonPath,
    [string]$RequirementsPath,
    [string]$Label
) {
    $created = $false
    if (-not (Test-Path $PythonPath -PathType Leaf)) {
        Write-Host "[$Label] creating virtual environment..."
        & $PythonBootstrap @PythonBootstrapArgs -m venv $VenvPath
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create $Label virtual environment."
        }
        $created = $true
    }

    if ($created -or $Setup) {
        Write-Host "[$Label] installing pinned dependencies..."
        & $PythonPath -m pip install --disable-pip-version-check -r $RequirementsPath
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to install $Label dependencies."
        }
    }
}

function Invoke-AIBench([string[]]$Arguments) {
    & go run ./cmd/ai-bench @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "ai-bench failed: $($Arguments -join ' ')"
    }
}

Write-Host "Validating fixture pack..."
Invoke-AIBench @(
    "validate-catalog",
    "-catalog", $Catalog,
    "-fixtures-dir", $FixtureDir
)

Ensure-Venv $OnnxVenv $OnnxPython $OnnxRequirements "ONNX Tagger"
if (-not $SkipPixAIV1) {
    Ensure-Venv $PixAIVenv $PixAIPython $PixAIRequirements "PixAI v1"
}

$RuntimeInspector = Join-Path $RepoRoot "benchmarks\ai\adapters\tagger_runtime_inspect.py"
$RuntimeInfoPath = Join-Path $ResultsDir "tagger-runtime-info.json"
$InspectionProfiles = @(
    (Join-Path $RepoRoot "benchmarks\ai\profiles\wd-vit-tagger-v3.json"),
    (Join-Path $RepoRoot "benchmarks\ai\profiles\pixai-tagger-v0.9.json"),
    (Join-Path $RepoRoot "benchmarks\ai\profiles\camie-tagger-v2.json"),
    (Join-Path $RepoRoot "benchmarks\ai\profiles\pixai-tagger-v1.0.json")
)
Write-Host "Inspecting pinned Tagger runtime metadata..."
& $OnnxPython $RuntimeInspector --out $RuntimeInfoPath @InspectionProfiles
if ($LASTEXITCODE -ne 0) {
    throw "Tagger runtime inspection failed."
}

$candidates = @(
    [PSCustomObject]@{
        Name = "wd-vit-tagger-v3"
        Profile = "benchmarks\ai\profiles\wd-vit-tagger-v3.json"
        Python = $OnnxPython
        Adapter = "benchmarks\ai\adapters\tagger_onnx.py"
        Result = "wd-vit-tagger-v3.json"
    },
    [PSCustomObject]@{
        Name = "pixai-tagger-v0.9"
        Profile = "benchmarks\ai\profiles\pixai-tagger-v0.9.json"
        Python = $OnnxPython
        Adapter = "benchmarks\ai\adapters\tagger_onnx.py"
        Result = "pixai-tagger-v0.9.json"
    },
    [PSCustomObject]@{
        Name = "camie-tagger-v2"
        Profile = "benchmarks\ai\profiles\camie-tagger-v2.json"
        Python = $OnnxPython
        Adapter = "benchmarks\ai\adapters\tagger_onnx.py"
        Result = "camie-tagger-v2.json"
    }
)

if (-not $SkipPixAIV1) {
    $candidates += [PSCustomObject]@{
        Name = "pixai-tagger-v1.0"
        Profile = "benchmarks\ai\profiles\pixai-tagger-v1.0.json"
        Python = $PixAIPython
        Adapter = "benchmarks\ai\adapters\tagger_pixai_v1.py"
        Result = "pixai-tagger-v1.0.json"
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
        "-adapter", $candidate.Python,
        "-adapter-arg", $adapterPath,
        "-fixtures-dir", $FixtureDir,
        "-hardware-id", $HardwareId,
        "-cpu", $Cpu,
        "-ram-bytes", $RamBytes.ToString(),
        "-lumine-version", $LumineVersion,
        "-case-timeout", $CaseTimeout,
        "-out", $resultPath
    )

    Write-Host "[$($candidate.Name)] validating result..."
    Invoke-AIBench @(
        "validate-result",
        "-catalog", $Catalog,
        "-result", $resultPath
    )
    $resultPaths += $resultPath
}

if ($resultPaths.Count -lt 2) {
    throw "At least two benchmark results are required for a comparison report."
}

$reporter = Join-Path $RepoRoot "benchmarks\ai\adapters\tagger_report.py"
$reportPath = Join-Path $ResultsDir "tagger-comparison.md"
Write-Host "Rendering comparison report..."
$reportOutput = & $OnnxPython $reporter @resultPaths
if ($LASTEXITCODE -ne 0) {
    throw "Failed to render tagger comparison report."
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
    runtimeInspection = (Split-Path $RuntimeInfoPath -Leaf)
    pixaiV1Skipped = [bool]$SkipPixAIV1
}
$runInfoPath = Join-Path $ResultsDir "tagger-run-info.json"
$runInfo | ConvertTo-Json -Depth 5 | Set-Content -Path $runInfoPath -Encoding UTF8

Write-Host ""
Write-Host "Tagger benchmark complete."
Write-Host "Comparison : $reportPath"
Write-Host "Runtime info: $RuntimeInfoPath"
Write-Host "Run info   : $runInfoPath"
if ($SkipPixAIV1) {
    Write-Warning "PixAI Tagger v1.0 was skipped. This run is useful for diagnostics, but it is not the full current Issue #165 candidate comparison."
}
