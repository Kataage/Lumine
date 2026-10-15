param(
    [int]$BenchmarkCount = 10000,
    [int]$CandidatePages = 2048,
    [switch]$ProactivePassiveCandidate,
    [ValidateSet(64, 128)][int]$ProactiveEveryWrites = 128,
    [string]$OutputDirectory = "artifacts/benchmarks/wal-checkpoint-comparison"
)

$ErrorActionPreference = "Stop"
if ($BenchmarkCount -ne 10000) {
    throw "WAL ABBA comparison requires exactly 10,000 individually persisted records per phase."
}
if (-not $ProactivePassiveCandidate -and $CandidatePages -notin @(512, 2048)) {
    throw "WAL ABBA candidates are restricted to 512 or 2048 pages; 1000 remains the production default."
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

# Strict library acceptance executes before this diagnostic. Neither
# candidate results nor a fortunate retry can replace its unchanged gate.
$profiles = @(
    @{ Name = "A1"; Pages = 1000 },
    @{ Name = "B1"; Pages = $(if ($ProactivePassiveCandidate) { 1000 } else { $CandidatePages }) },
    @{ Name = "B2"; Pages = $(if ($ProactivePassiveCandidate) { 1000 } else { $CandidatePages }) },
    @{ Name = "A2"; Pages = 1000 }
)
$samples = @()
foreach ($profile in $profiles) {
    $resultPath = Join-Path $OutputDirectory ("wal-" + $profile.Name + ".json")
    $proactiveEvery = if ($ProactivePassiveCandidate -and $profile.Name -match "^B") { $ProactiveEveryWrites } else { 0 }
    & dotnet run --project tools/Lumine.Library.Benchmarks/Lumine.Library.Benchmarks.csproj -c Release --no-build -- --count $BenchmarkCount --wal-autocheckpoint-pages $profile.Pages --proactive-passive-checkpoint-every $proactiveEvery --output $resultPath
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
    $walNoopBusy = [int]$result.metadata.technical_metadata_wal_noop_busy_after_single
    $walNoopFrames = [int]$result.metadata.technical_metadata_wal_noop_log_frames_after_single
    $walNoopCheckpointed = [int]$result.metadata.technical_metadata_wal_noop_checkpointed_frames_after_single
    $walNoopOutstanding = [int]$result.metadata.technical_metadata_wal_noop_outstanding_frames_after_single
    if (-not (
        ($walNoopBusy -eq 1 -and $walNoopFrames -eq -1 -and
         $walNoopCheckpointed -eq -1 -and $walNoopOutstanding -eq -1) -or
        ($walNoopBusy -in @(0, 1) -and $walNoopFrames -ge 0 -and
         $walNoopCheckpointed -ge 0 -and $walNoopCheckpointed -le $walNoopFrames -and
         $walNoopOutstanding -eq ($walNoopFrames - $walNoopCheckpointed))
    )) {
        throw "WAL ABBA observational NOOP frame status inconsistent for $($profile.Name)."
    }
    $observedEvery = [int]$result.metadata.proactive_passive_checkpoint_every_writes
    $proactiveStarted = [int]$result.metadata.proactive_passive_checkpoint_requested
    $proactiveCompleted = [int]$result.metadata.proactive_passive_checkpoint_completed
    $proactiveBusy = [int]$result.metadata.proactive_passive_checkpoint_busy
    $proactiveUnknown = [int]$result.metadata.proactive_passive_checkpoint_unknown_progress
    $proactivePartial = [int]$result.metadata.proactive_passive_checkpoint_partial
    $maxPendingFrames = [int]$result.metadata.proactive_passive_checkpoint_max_outstanding_wal_frames
    $reportedCheckpointFrames = [long]$result.metadata.proactive_passive_checkpointed_frames_reported_total
    if ($proactiveUnknown -lt 0 -or $proactiveUnknown -gt $proactiveBusy -or
        $proactivePartial -lt 0 -or
        ($proactivePartial + $proactiveUnknown) -gt $proactiveCompleted -or
        $maxPendingFrames -lt 0 -or $reportedCheckpointFrames -lt 0 -or
        ($proactiveEvery -eq 0 -and
            ($proactiveUnknown -ne 0 -or $proactivePartial -ne 0 -or
             $maxPendingFrames -ne 0 -or $reportedCheckpointFrames -ne 0))) {
        throw "WAL ABBA PASSIVE checkpoint frame progress metrics are inconsistent for $($profile.Name)."
    }
    if ($observedEvery -ne $proactiveEvery -or
        $proactiveStarted -ne $proactiveCompleted -or
        $proactiveBusy -gt $proactiveCompleted -or
        ($proactiveEvery -gt 0 -and $proactiveStarted -le 0) -or
        ($proactiveEvery -eq 0 -and $proactiveStarted -ne 0)) {
        throw "WAL ABBA proactive PASSIVE checkpoint diagnostics invalid for $($profile.Name)."
    }
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
        wal_noop_busy = $walNoopBusy
        wal_noop_frames = $walNoopFrames
        wal_noop_checkpointed = $walNoopCheckpointed
        wal_noop_outstanding = $walNoopOutstanding
        proactive_passive_every = $observedEvery
        proactive_passive_started = $proactiveStarted
        proactive_passive_completed = $proactiveCompleted
        proactive_passive_busy = $proactiveBusy
        proactive_passive_unknown_progress = $proactiveUnknown
        proactive_passive_partial = $proactivePartial
        proactive_max_outstanding_frames = $maxPendingFrames
        proactive_checkpointed_frames_reported_total = $reportedCheckpointFrames
        proactive_passive_max_ms = [long]$result.metadata.proactive_passive_checkpoint_max_ticks / 10000.0
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
    order = $(if ($ProactivePassiveCandidate) { "auto1000,auto1000+passive$ProactiveEveryWrites,auto1000+passive$ProactiveEveryWrites,auto1000" } else { "1000,$CandidatePages,$CandidatePages,1000" })
    candidate_pages = $(if ($ProactivePassiveCandidate) { 1000 } else { $CandidatePages })
    proactive_passive_candidate = [bool]$ProactivePassiveCandidate
    proactive_passive_candidate_every_writes = $(if ($ProactivePassiveCandidate) { $ProactiveEveryWrites } else { 0 })
    samples = $samples
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Host "WAL ABBA comparison completed (diagnostic only, not an adoption decision): $reportPath"
