param(
    [string]$HardwareId = "",

    [string]$Cpu = "",
    [Int64]$RamBytes = 0,
    [string]$ResultsDir = "",
    [string]$CaseTimeout = "20m",
    [switch]$Setup,
    [switch]$InspectOnly,
    [switch]$ApplyPins,
    [switch]$AllowLlamaServerOverride
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Set-Location $RepoRoot

if ($ApplyPins -and -not $InspectOnly) {
    throw "-ApplyPins is only valid together with -InspectOnly."
}

if (-not $AllowLlamaServerOverride -and -not [string]::IsNullOrWhiteSpace($env:LUMINE_PROMPT_LLAMA_SERVER)) {
    throw "LUMINE_PROMPT_LLAMA_SERVER is set. Controlled Prompt Engine evidence must use the pinned runtime. Clear it or pass -AllowLlamaServerOverride for debugging only."
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

$Catalog = Join-Path $RepoRoot "benchmarks\ai\catalogs\prompt-engine-v1.json"
$Requirements = Join-Path $RepoRoot "benchmarks\ai\adapters\requirements-prompt.txt"
$Venv = Join-Path $RepoRoot ".venv-prompt-bench"
$Python = Join-Path $Venv "Scripts\python.exe"
$Adapter = Join-Path $RepoRoot "benchmarks\ai\adapters\prompt_llamacpp.py"
$Reporter = Join-Path $RepoRoot "benchmarks\ai\adapters\prompt_report.py"
$PinInspector = Join-Path $RepoRoot "benchmarks\ai\adapters\prompt_pin_inspect.py"

function Ensure-Venv {
    $created = $false
    if (-not (Test-Path $Python -PathType Leaf)) {
        Write-Host "[Prompt Engine] creating virtual environment..."
        & $PythonBootstrap @PythonBootstrapArgs -m venv $Venv
        if ($LASTEXITCODE -ne 0) { throw "Failed to create Prompt Engine virtual environment." }
        $created = $true
    }
    if ($created -or $Setup) {
        Write-Host "[Prompt Engine] installing pinned dependencies..."
        & $Python -m pip install --disable-pip-version-check -r $Requirements
        if ($LASTEXITCODE -ne 0) { throw "Failed to install Prompt Engine dependencies." }
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
    return (
        ([string]$profile.version) -ne "main" -and
        -not [string]::IsNullOrWhiteSpace([string]$profile.artifactSha256) -and
        [Int64]$profile.modelSizeBytes -gt 0 -and
        -not [string]::IsNullOrWhiteSpace($modelHash)
    )
}

Ensure-Venv

$candidates = @(
    [PSCustomObject]@{ Name="neohorse"; Profile="benchmarks\ai\profiles\prompt-neohorse-1-4b-abliterated-q4.json"; Result="prompt-neohorse.json" },
    [PSCustomObject]@{ Name="spark-x2.5-heretic-jp"; Profile="benchmarks\ai\profiles\prompt-spark-x2.5-4b-heretic-jp-q4.json"; Result="prompt-spark.json" },
    [PSCustomObject]@{ Name="qwen3.5-4b-abliterated"; Profile="benchmarks\ai\profiles\prompt-qwen3.5-4b-abliterated-q4.json"; Result="prompt-qwen35-abliterated.json" },
    [PSCustomObject]@{ Name="qwen3.5-4b-control"; Profile="benchmarks\ai\profiles\prompt-qwen3.5-4b-control-q4.json"; Result="prompt-qwen35-control.json" }
)

$pinInfoPath = Join-Path $ResultsDir "prompt-pin-info.json"
$profilePaths = @($candidates | ForEach-Object { Join-Path $RepoRoot $_.Profile })
$inspectArgs = @($PinInspector, "--out", $pinInfoPath)
if ($ApplyPins) {
    $inspectArgs += "--apply"
}
$inspectArgs += $profilePaths

Write-Host "Resolving immutable Prompt candidate metadata..."
& $Python @inspectArgs
if ($LASTEXITCODE -ne 0) {
    throw "Prompt candidate pin inspection failed."
}

if ($InspectOnly) {
    if ($ApplyPins) {
        foreach ($candidate in $candidates) {
            $profilePath = Join-Path $RepoRoot $candidate.Profile
            if (-not (Test-ProfilePinned $profilePath)) {
                throw "Applied Prompt profile is still not fully pinned: $($candidate.Profile)"
            }
        }
    }

    Write-Host ""
    Write-Host "Prompt pin inspection complete."
    Write-Host "Pin info: $pinInfoPath"
    if ($ApplyPins) {
        Write-Host "Profiles were explicitly updated and revalidated as immutable."
    } else {
        Write-Host "Profiles were not modified. Re-run with -InspectOnly -ApplyPins to apply them explicitly."
    }
    return
}

if ([string]::IsNullOrWhiteSpace($HardwareId)) {
    throw "-HardwareId is required unless -InspectOnly is used."
}
if ([string]::IsNullOrWhiteSpace($Cpu)) {
    $Cpu = ((Get-CimInstance Win32_Processor | ForEach-Object { $_.Name.Trim() }) -join " + ")
}
if ($RamBytes -le 0) {
    $RamBytes = [Int64](Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
}
$LumineVersion = (& git rev-parse HEAD).Trim()

Write-Host ""
Write-Host "Benchmark environment"
Write-Host "  Hardware ID : $HardwareId"
Write-Host "  CPU         : $Cpu"
Write-Host "  RAM bytes   : $RamBytes"
Write-Host "  Lumine      : $LumineVersion"
Write-Host "  Results     : $ResultsDir"
Write-Host ""

$resultPaths = @()
$allPinned = $true

foreach ($candidate in $candidates) {
    $profilePath = Join-Path $RepoRoot $candidate.Profile
    $resultPath = Join-Path $ResultsDir $candidate.Result
    $pinned = Test-ProfilePinned $profilePath
    if (-not $pinned) {
        $allPinned = $false
        Write-Warning "[$($candidate.Name)] profile is exploratory; this run is discovery, not adoption evidence."
    }

    Write-Host "[$($candidate.Name)] running controlled CPU benchmark..."
    Invoke-AIBench @(
        "run",
        "-catalog", $Catalog,
        "-profile", $profilePath,
        "-adapter", $Python,
        "-adapter-arg", $Adapter,
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

$reportPath = Join-Path $ResultsDir "prompt-comparison.md"
$reportOutput = & $Python $Reporter @resultPaths
if ($LASTEXITCODE -ne 0) { throw "Failed to render Prompt Engine comparison report." }
$reportOutput | Set-Content -Path $reportPath -Encoding UTF8

$runInfo = [ordered]@{
    schemaVersion = 1
    generatedAt = [DateTime]::UtcNow.ToString("o")
    hardwareId = $HardwareId
    cpu = $Cpu
    ramBytes = $RamBytes
    lumineVersion = $LumineVersion
    caseTimeout = $CaseTimeout
    evidenceReady = [bool]$allPinned
    candidates = @($candidates | ForEach-Object { $_.Name })
    resultFiles = @($resultPaths | ForEach-Object { Split-Path $_ -Leaf })
    comparisonReport = (Split-Path $reportPath -Leaf)
    pinInfo = (Split-Path $pinInfoPath -Leaf)
}
$runInfoPath = Join-Path $ResultsDir "prompt-run-info.json"
$runInfo | ConvertTo-Json -Depth 5 | Set-Content -Path $runInfoPath -Encoding UTF8

Write-Host ""
Write-Host "Prompt Engine benchmark complete."
Write-Host "Comparison: $reportPath"
Write-Host "Pin info  : $pinInfoPath"
Write-Host "Run info  : $runInfoPath"
if (-not $allPinned) {
    Write-Warning "One or more profiles are exploratory. Pin immutable revisions/hashes from prompt-pin-info.json and rerun before using results as adoption evidence."
}
