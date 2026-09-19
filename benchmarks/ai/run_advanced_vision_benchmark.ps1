param(
    [Parameter(Mandatory = $true)]
    [string]$FixtureDir,

    [Parameter(Mandatory = $true)]
    [string]$HardwareId,

    [string]$Cpu = "",
    [Int64]$RamBytes = 0,
    [string]$ResultsDir = "",
    [string]$CaseTimeout = "20m",
    [switch]$Setup,
    [switch]$RunHereticDiscovery,
    [switch]$AllowLlamaServerOverride
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Set-Location $RepoRoot

if (-not (Test-Path $FixtureDir -PathType Container)) {
    throw "FixtureDir does not exist: $FixtureDir"
}
$FixtureDir = (Resolve-Path $FixtureDir).Path

if (-not $AllowLlamaServerOverride -and -not [string]::IsNullOrWhiteSpace($env:LUMINE_LLAMA_SERVER)) {
    throw "LUMINE_LLAMA_SERVER is set. Controlled Advanced Vision evidence must use the pinned runtime. Clear it or pass -AllowLlamaServerOverride for debugging only."
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

$Catalog = Join-Path $RepoRoot "benchmarks\ai\catalogs\advanced-vision-v1.json"
$Requirements = Join-Path $RepoRoot "benchmarks\ai\adapters\requirements-advancedvision.txt"
$Venv = Join-Path $RepoRoot ".venv-advancedvision-bench"
$Python = Join-Path $Venv "Scripts\python.exe"
$Adapter = Join-Path $RepoRoot "benchmarks\ai\adapters\advancedvision_llamacpp.py"
$Reporter = Join-Path $RepoRoot "benchmarks\ai\adapters\advancedvision_report.py"

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

function Get-OptionalProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Test-ProfilePinned([string]$ProfilePath) {
    $profile = Get-Content $ProfilePath -Raw | ConvertFrom-Json
    $modelHash = [string]$profile.parameters.modelSha256
    $mmprojHash = [string]$profile.parameters.mmprojSha256
    return (
        ([string]$profile.version) -ne "main" -and
        -not [string]::IsNullOrWhiteSpace([string]$profile.artifactSha256) -and
        [Int64]$profile.modelSizeBytes -gt 0 -and
        -not [string]::IsNullOrWhiteSpace($modelHash) -and
        -not [string]::IsNullOrWhiteSpace($mmprojHash)
    )
}

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

Write-Host "Validating Advanced Vision fixture pack..."
Invoke-AIBench @(
    "validate-catalog",
    "-catalog", $Catalog,
    "-fixtures-dir", $FixtureDir
)
Ensure-Venv

$candidates = @(
    [PSCustomObject]@{ Name="qwen3-vl-2b"; Profile="benchmarks\ai\profiles\advanced-qwen3-vl-2b-q4.json"; Result="advanced-qwen3-vl-2b-q4.json" },
    [PSCustomObject]@{ Name="qwen3-vl-2b-abliterated"; Profile="benchmarks\ai\profiles\advanced-qwen3-vl-2b-abliterated-q4.json"; Result="advanced-qwen3-vl-2b-abliterated-q4.json" },
    [PSCustomObject]@{ Name="internvl3.5-2b"; Profile="benchmarks\ai\profiles\advanced-internvl3.5-2b-q4.json"; Result="advanced-internvl3.5-2b-q4.json" },
    [PSCustomObject]@{ Name="smolvlm2-2.2b"; Profile="benchmarks\ai\profiles\advanced-smolvlm2-2.2b-q4.json"; Result="advanced-smolvlm2-2.2b-q4.json" },
    [PSCustomObject]@{ Name="minicpm-v4.6"; Profile="benchmarks\ai\profiles\advanced-minicpm-v4.6-q4.json"; Result="advanced-minicpm-v4.6-q4.json" },
    [PSCustomObject]@{ Name="granite-vision-4.1-4b"; Profile="benchmarks\ai\profiles\advanced-granite-vision-4.1-4b-q4.json"; Result="advanced-granite-vision-4.1-4b-q4.json" }
)

$hereticProfile = Join-Path $RepoRoot "benchmarks\ai\profiles\advanced-qwen3-vl-2b-heretic-q4.json"
$hereticPinned = Test-ProfilePinned $hereticProfile
if ($hereticPinned) {
    $candidates += [PSCustomObject]@{
        Name="qwen3-vl-2b-heretic"
        Profile="benchmarks\ai\profiles\advanced-qwen3-vl-2b-heretic-q4.json"
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

$resultPaths = @()
foreach ($candidate in $candidates) {
    $resultPaths += Run-Candidate $candidate.Name $candidate.Profile $candidate.Result
}

if (-not $hereticPinned -and $RunHereticDiscovery) {
    Write-Warning "Heretic profile is exploratory. Its result will NOT be included in the evidence comparison report."
    $discoveryPath = Run-Candidate "qwen3-vl-2b-heretic-discovery" "benchmarks\ai\profiles\advanced-qwen3-vl-2b-heretic-q4.json" "advanced-qwen3-vl-2b-heretic-discovery.json"
    $discovery = Get-Content $discoveryPath -Raw | ConvertFrom-Json
    $modelSizeCase = $discovery.cases | Where-Object { $_.fixtureId -eq "advanced-perf-model-size-001" } | Select-Object -First 1
    if ($null -ne $modelSizeCase -and $null -ne $modelSizeCase.output) {
        $pinInfoPath = Join-Path $ResultsDir "advanced-heretic-pin-info.json"
        $modelSizeCase.output | ConvertTo-Json -Depth 10 | Set-Content -Path $pinInfoPath -Encoding UTF8
        Write-Host "Heretic pin info: $pinInfoPath"
    }
} elseif (-not $hereticPinned) {
    Write-Warning "Heretic profile is not immutably pinned and was excluded. Use -RunHereticDiscovery to resolve pin data."
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
