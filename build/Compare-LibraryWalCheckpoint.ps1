param(
    [int]$BenchmarkCount = 10000,
    [int]$CandidatePages = 2048,
    [string]$OutputDirectory = "artifacts/benchmarks/wal-checkpoint-comparison"
)

$ErrorActionPreference = "Stop"
if ($BenchmarkCount -ne 10000) {
    throw "WAL ABBA comparison requires exactly 10,000 individually persisted records per phase."
}
if ($CandidatePages -notin @(512, 2048)) {
    throw "WAL ABBA candidates are restricted to 512 or 2048 pages; 1000 remains the production default."
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

# Strict library acceptance executes before this diagnostic. Neither
# candidate results nor a fortunate retry can replace its unchanged gate.
$profiles = @(
    @{ Name = "A1"; Pages = 1000 },
    @{ Name = "B1"; Pages = $CandidatePages },
    @{ Name = "B2"; Pages = $CandidatePages },
    @{ Name = "A2"; Pages = 1000 }
)
$samples = @()
foreach ($profile in $profiles) {
    $resultPath = Join-Path $OutputDirectory ("wal-" + $profile.Name + ".json")
    & dotnet run --project tools/Lumine.Library.Benchmarks/Lumine.Library.Benchmarks.csproj -c Release --no-build -- --count $BenchmarkCount --wal-autocheckpoint-pages $profile.Pages --output $resultPath
    if ($LASTEXITCODE -ne 0) {
        throw "WAL ABBA diagnostic failed for $($profile.Name)."
    }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $metric = @($result.measurements | Where-Object { $_.name -eq "library.technical_metadata_persist" })
    $count = [int]$result.metadata.technical_metadata_persist_count
    $sampleCount = [int]$result.metadata.technical_metadata_phase_observed_write_count
    $pages = [int]$result.metadata.wal_autocheckpoint_pages
    $duration = if ($metric.Count -eq 1) { [double]$metric[0].durationMs } else { -1 }
    $commitMs = [long]$result.metadata.technical_metadata_commit_ticks / 10000.0
    $maxCommitMs = [long]$result.metadata.technical_metadata_commit_max_ticks / 10000.0
    $slow32 = [int]$result.metadata.technical_metadata_commit_bucket_32_to_lt_128ms + [int]$result.metadata.technical_metadata_commit_bucket_ge_128ms
    $walBytes = [long]$result.metadata.technical_metadata_wal_bytes_after_single
    if ($count -ne 10000 -or $sampleCount -ne 10000 -or $pages -ne $profile.Pages -or
        $duration -le 0 -or $commitMs -le 0 -or $maxCommitMs -lt 0 -or
        $slow32 -lt 0 -or $walBytes -lt 0 -or $commitMs -gt $duration) {
        throw "WAL ABBA diagnostic values invalid for $($profile.Name)."
    }
    $sample = [pscustomobject]@{
        phase = $profile.Name
        pages = $pages
        persisted = $count
        elapsed_ms = $duration
        commit_total_ms = $commitMs
        commit_max_ms = $maxCommitMs
        commit_ge_32ms = $slow32
        wal_bytes_after_single = $walBytes
    }
    $samples += $sample
    Write-Host ("WAL checkpoint {0}: pages={1}; 10k individual={2:F1}ms; COMMIT={3:F1}ms; >=32ms={4}; WAL={5:N0}B" -f
        $sample.phase, $sample.pages, $sample.elapsed_ms, $sample.commit_total_ms, $sample.commit_ge_32ms, $sample.wal_bytes_after_single)
}

$reportPath = Join-Path $OutputDirectory "wal-abba-summary.json"
$report = [pscustomobject]@{
    kind = "library-wal-autocheckpoint-abba-diagnostic"
    original_acceptance_gate_unchanged = $true
    independently_committed_writes_per_phase = 10000
    order = "1000,$CandidatePages,$CandidatePages,1000"
    candidate_pages = $CandidatePages
    samples = $samples
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Host "WAL ABBA comparison completed (diagnostic only, not an adoption decision): $reportPath"
