param(
    [string]$ResultsDir = "",
    [string]$HardwareId = "",
    [string]$SigLIPBenchTime = "10x",
    [string]$LlamaBenchTime = "5x"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Set-Location $RepoRoot

function Require-Command([string]$Name) {
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $cmd) {
        throw "Required command is not available: $Name"
    }
    return $cmd.Source
}

function Resolve-NvidiaSmi {
    $cmd = Get-Command "nvidia-smi" -ErrorAction SilentlyContinue
    if ($null -ne $cmd) {
        return $cmd.Source
    }
    $fallbacks = @(
        (Join-Path $env:WINDIR "System32\nvidia-smi.exe"),
        "C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe"
    )
    foreach ($candidate in $fallbacks) {
        if (Test-Path $candidate -PathType Leaf) {
            return $candidate
        }
    }
    throw "nvidia-smi was not found. The physical GPU acceptance runner requires an NVIDIA driver installation so peak dedicated VRAM can be recorded."
}

function Get-GpuSampleStats([string]$Path) {
    if (-not (Test-Path $Path -PathType Leaf)) {
        return [PSCustomObject]@{
            minMemoryMiB = $null
            peakMemoryMiB = $null
            deltaMemoryMiB = $null
            peakUtilizationPercent = $null
        }
    }
    $rows = @(Import-Csv $Path)
    if ($rows.Count -eq 0) {
        return [PSCustomObject]@{
            minMemoryMiB = $null
            peakMemoryMiB = $null
            deltaMemoryMiB = $null
            peakUtilizationPercent = $null
        }
    }

    $memory = @($rows | ForEach-Object { [double]$_.MemoryUsedMiB })
    $util = @($rows | ForEach-Object { [double]$_.UtilizationPercent })
    $minMemory = ($memory | Measure-Object -Minimum).Minimum
    $peakMemory = ($memory | Measure-Object -Maximum).Maximum
    $peakUtil = ($util | Measure-Object -Maximum).Maximum

    return [PSCustomObject]@{
        minMemoryMiB = $minMemory
        peakMemoryMiB = $peakMemory
        deltaMemoryMiB = ($peakMemory - $minMemory)
        peakUtilizationPercent = $peakUtil
    }
}

function Start-GpuSampler([string]$Name, [string]$OutputPath, [string]$NvidiaSmiPath) {
    $sentinel = Join-Path $ResultsDir ".$Name.gpu-sampling"
    Set-Content -Path $sentinel -Value "running" -Encoding ascii
    "Timestamp,Index,Name,DriverVersion,MemoryUsedMiB,MemoryTotalMiB,UtilizationPercent" |
        Set-Content -Path $OutputPath -Encoding utf8

    $job = Start-Job -ScriptBlock {
        param($SentinelPath, $CsvPath, $SmiPath)
        while (Test-Path $SentinelPath -PathType Leaf) {
            $stamp = [DateTime]::UtcNow.ToString("o")
            $lines = @(& $SmiPath --query-gpu=index,name,driver_version,memory.used,memory.total,utilization.gpu --format=csv,noheader,nounits 2>$null)
            foreach ($line in $lines) {
                if (-not [string]::IsNullOrWhiteSpace($line)) {
                    Add-Content -Path $CsvPath -Value "$stamp,$line" -Encoding utf8
                }
            }
            Start-Sleep -Milliseconds 500
        }
    } -ArgumentList $sentinel, $OutputPath, $NvidiaSmiPath

    return [PSCustomObject]@{
        job = $job
        sentinel = $sentinel
    }
}

function Stop-GpuSampler($Sampler) {
    if ($null -eq $Sampler) {
        return
    }
    Remove-Item $Sampler.sentinel -Force -ErrorAction SilentlyContinue
    Wait-Job $Sampler.job -Timeout 10 | Out-Null
    Receive-Job $Sampler.job -ErrorAction SilentlyContinue | Out-Null
    Remove-Job $Sampler.job -Force -ErrorAction SilentlyContinue
}

function Invoke-GoEvidence(
    [string]$Name,
    [string[]]$Arguments,
    [hashtable]$Environment
) {
    $logPath = Join-Path $ResultsDir "$Name.log"
    $gpuPath = Join-Path $ResultsDir "$Name-gpu.csv"
    $oldEnvironment = @{}

    foreach ($key in $Environment.Keys) {
        $oldEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
        [Environment]::SetEnvironmentVariable($key, [string]$Environment[$key], "Process")
    }

    $sampler = Start-GpuSampler -Name $Name -OutputPath $gpuPath -NvidiaSmiPath $NvidiaSmi
    $startedAt = [DateTime]::UtcNow
    $exitCode = -1

    try {
        Write-Host ""
        Write-Host "=== $Name ==="
        Write-Host "go $($Arguments -join ' ')"
        & go @Arguments 2>&1 | Tee-Object -FilePath $logPath
        $exitCode = $LASTEXITCODE
    } catch {
        $_ | Out-String | Add-Content -Path $logPath -Encoding utf8
        Write-Host $_
        $exitCode = 1
    } finally {
        Stop-GpuSampler $sampler
        foreach ($key in $Environment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $oldEnvironment[$key], "Process")
        }
    }

    $finishedAt = [DateTime]::UtcNow
    $stats = Get-GpuSampleStats $gpuPath
    $benchmarkLines = @()
    $providerLines = @()
    if (Test-Path $logPath -PathType Leaf) {
        $benchmarkLines = @(
            Get-Content $logPath |
                Where-Object { $_ -match '^Benchmark[A-Za-z0-9_/-]+' }
        )
        $providerLines = @(
            Get-Content $logPath |
                Where-Object {
                    $_ -match '(?i)directml|execution provider|provider=|vulkan|offloaded\s+\d+/\d+\s+layers'
                } |
                Select-Object -Last 30
        )
    }

    return [PSCustomObject]@{
        name = $Name
        passed = ($exitCode -eq 0)
        exitCode = $exitCode
        startedAt = $startedAt.ToString("o")
        finishedAt = $finishedAt.ToString("o")
        durationSeconds = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
        log = (Split-Path $logPath -Leaf)
        gpuSamples = (Split-Path $gpuPath -Leaf)
        minVramMiB = $stats.minMemoryMiB
        peakVramMiB = $stats.peakMemoryMiB
        deltaVramMiB = $stats.deltaMemoryMiB
        peakGpuUtilizationPercent = $stats.peakUtilizationPercent
        benchmarkLines = $benchmarkLines
        providerEvidence = $providerLines
    }
}

$Go = Require-Command "go"
$Git = Require-Command "git"
$NvidiaSmi = Resolve-NvidiaSmi

if ([string]::IsNullOrWhiteSpace($ResultsDir)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $ResultsDir = Join-Path $RepoRoot "benchmarks\ai\results\gpu-acceptance\$stamp"
}
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
$ResultsDir = (Resolve-Path $ResultsDir).Path

if ([string]::IsNullOrWhiteSpace($HardwareId)) {
    $HardwareId = $env:COMPUTERNAME
}
if ([string]::IsNullOrWhiteSpace($HardwareId)) {
    $HardwareId = "physical-windows-gpu"
}

$gitCommit = (& $Git rev-parse HEAD).Trim()
$cpu = ((Get-CimInstance Win32_Processor | ForEach-Object { $_.Name.Trim() }) -join " + ")
$ramBytes = [Int64](Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
$os = Get-CimInstance Win32_OperatingSystem
$videoControllers = @(
    Get-CimInstance Win32_VideoController |
        ForEach-Object {
            [PSCustomObject]@{
                name = $_.Name
                driverVersion = $_.DriverVersion
                adapterRam = $_.AdapterRAM
            }
        }
)

& $NvidiaSmi | Set-Content -Path (Join-Path $ResultsDir "nvidia-smi.txt") -Encoding utf8
& $NvidiaSmi --query-gpu=index,name,uuid,driver_version,memory.total,pci.bus_id --format=csv,noheader |
    Set-Content -Path (Join-Path $ResultsDir "nvidia-gpus.csv") -Encoding utf8

$hardware = [ordered]@{
    schemaVersion = 1
    hardwareId = $HardwareId
    generatedAt = [DateTime]::UtcNow.ToString("o")
    gitCommit = $gitCommit
    computerName = $env:COMPUTERNAME
    osCaption = $os.Caption
    osVersion = $os.Version
    cpu = $cpu
    ramBytes = $ramBytes
    videoControllers = $videoControllers
    goVersion = (& $Go version)
}
$hardware | ConvertTo-Json -Depth 8 |
    Set-Content -Path (Join-Path $ResultsDir "hardware.json") -Encoding utf8

$llamaCache = Join-Path $RepoRoot ".lumine-ai-bench-cache"
New-Item -ItemType Directory -Force -Path $llamaCache | Out-Null

$results = @()

$results += Invoke-GoEvidence -Name "siglip2-directml-smoke" -Arguments @("test", "./internal/ai/siglip2", "-run", "TestRealSigLIP2DirectMLSmoke", "-count=1", "-v") -Environment @{ LUMINE_SIGLIP2_REAL_GPU_SMOKE = "1" }
$results += Invoke-GoEvidence -Name "siglip2-cpu-benchmark" -Arguments @("test", "./internal/ai/siglip2", "-run=^$", "-bench=^BenchmarkRealSigLIP2ImageCPU$", "-benchtime=$SigLIPBenchTime", "-benchmem") -Environment @{ LUMINE_SIGLIP2_REAL_BENCH = "1" }
$results += Invoke-GoEvidence -Name "siglip2-directml-benchmark" -Arguments @("test", "./internal/ai/siglip2", "-run=^$", "-bench=^BenchmarkRealSigLIP2ImageDirectML$", "-benchtime=$SigLIPBenchTime", "-benchmem") -Environment @{ LUMINE_SIGLIP2_REAL_GPU_BENCH = "1" }
$results += Invoke-GoEvidence -Name "llamacpp-vulkan-device-smoke" -Arguments @("test", "./internal/ai/llamacpp", "-run", "TestRealVulkanDeviceSmoke", "-count=1", "-v") -Environment @{ LUMINE_LLAMA_RUNTIME_REAL_GPU_SMOKE = "1" }
$results += Invoke-GoEvidence -Name "llamacpp-smolvlm-vulkan-smoke" -Arguments @("test", "./internal/ai/llamacpp", "-run", "TestRealSmolVLMVulkanOffload", "-count=1", "-v") -Environment @{ LUMINE_LLAMA_REAL_CACHE = $llamaCache; LUMINE_LLAMA_REAL_GPU_MODEL_SMOKE = "1" }
$results += Invoke-GoEvidence -Name "llamacpp-smolvlm-cpu-benchmark" -Arguments @("test", "./internal/ai/llamacpp", "-run=^$", "-bench=^BenchmarkRealSmolVLMImageCPU$", "-benchtime=$LlamaBenchTime", "-benchmem") -Environment @{ LUMINE_LLAMA_REAL_CACHE = $llamaCache; LUMINE_LLAMA_REAL_BENCH = "1" }
$results += Invoke-GoEvidence -Name "llamacpp-smolvlm-vulkan-benchmark" -Arguments @("test", "./internal/ai/llamacpp", "-run=^$", "-bench=^BenchmarkRealSmolVLMImageVulkan$", "-benchtime=$LlamaBenchTime", "-benchmem") -Environment @{ LUMINE_LLAMA_REAL_CACHE = $llamaCache; LUMINE_LLAMA_REAL_GPU_BENCH = "1" }

$summary = [ordered]@{
    schemaVersion = 1
    generatedAt = [DateTime]::UtcNow.ToString("o")
    hardwareId = $HardwareId
    gitCommit = $gitCommit
    siglipBenchTime = $SigLIPBenchTime
    llamaBenchTime = $LlamaBenchTime
    hardware = $hardware
    results = $results
    allPassed = -not ($results | Where-Object { -not $_.passed })
}
$summaryPath = Join-Path $ResultsDir "gpu-acceptance.json"
$summary | ConvertTo-Json -Depth 12 |
    Set-Content -Path $summaryPath -Encoding utf8

$report = @()
$report += "# Lumine physical GPU acceptance"
$report += ""
$report += "- Hardware ID: $HardwareId"
$report += "- Git commit: $gitCommit"
$report += "- CPU: $cpu"
$report += "- RAM: $([Math]::Round($ramBytes / 1GB, 2)) GiB"
$report += "- Generated: $($summary.generatedAt)"
$report += ""
$report += "| Check | Result | Peak VRAM MiB | VRAM delta MiB | Peak GPU util % |"
$report += "| --- | --- | ---: | ---: | ---: |"
foreach ($result in $results) {
    $status = if ($result.passed) { "PASS" } else { "FAIL" }
    $peak = if ($null -eq $result.peakVramMiB) { "-" } else { [Math]::Round([double]$result.peakVramMiB, 1) }
    $delta = if ($null -eq $result.deltaVramMiB) { "-" } else { [Math]::Round([double]$result.deltaVramMiB, 1) }
    $util = if ($null -eq $result.peakGpuUtilizationPercent) { "-" } else { [Math]::Round([double]$result.peakGpuUtilizationPercent, 1) }
    $report += "| $($result.name) | $status | $peak | $delta | $util |"
}

$report += ""
$report += "## Benchmark output"
foreach ($result in $results) {
    if ($result.benchmarkLines.Count -eq 0) {
        continue
    }
    $report += ""
    $report += "### $($result.name)"
    $report += ""
    $report += "~~~text"
    $report += $result.benchmarkLines
    $report += "~~~"
}

$report += ""
$report += "## Provider / offload evidence"
foreach ($result in $results) {
    if ($result.providerEvidence.Count -eq 0) {
        continue
    }
    $report += ""
    $report += "### $($result.name)"
    $report += ""
    $report += "~~~text"
    $report += $result.providerEvidence
    $report += "~~~"
}

$reportPath = Join-Path $ResultsDir "gpu-acceptance.md"
$report -join [Environment]::NewLine |
    Set-Content -Path $reportPath -Encoding utf8

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value ($report -join [Environment]::NewLine) -Encoding utf8
}

Write-Host ""
Write-Host "GPU acceptance evidence written to:"
Write-Host "  $ResultsDir"

$failed = @($results | Where-Object { -not $_.passed })
if ($failed.Count -gt 0) {
    Write-Host ""
    Write-Host "Failed checks:"
    foreach ($item in $failed) {
        Write-Host "  - $($item.name) (exit $($item.exitCode))"
    }
    exit 1
}

Write-Host ""
Write-Host "All physical GPU acceptance checks passed."
