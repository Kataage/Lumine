param(
    [Parameter(Mandatory = $true)]
    [string]$Exe,

    [Parameter(Mandatory = $true)]
    [string]$Library,

    [string]$OutputDirectory = (Join-Path $PWD "artifacts/real-library-acceptance"),

    [int]$MinimumAssets = 1000,

    [int]$BrowseSeconds = 60,

    [int]$IdleSeconds = 10,

    [double]$MaxFastScrollMs = 1500,

    [string]$HardwareId = $env:COMPUTERNAME,

    [string]$Revision = "",

    [ValidateSet("Default", "RedirectionSurface")]
    [string]$Win32CompositionMode = "Default"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "Real-library Core acceptance requires Windows."
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw "Real-library Core acceptance requires Windows x64."
}

$exePath = (Resolve-Path -LiteralPath $Exe).Path
$libraryPath = (Resolve-Path -LiteralPath $Library).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$dataRoot = Join-Path $outputRoot "data"
$coldResultPath = Join-Path $outputRoot "cold.json"
$warmResultPath = Join-Path $outputRoot "warm.json"
$summaryPath = Join-Path $outputRoot "summary.json"

function Normalize-ComparisonPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)

    while ($full.Length -gt $root.Length) {
        $endsWithSeparator =
            $full.EndsWith([IO.Path]::DirectorySeparatorChar.ToString()) -or
            $full.EndsWith([IO.Path]::AltDirectorySeparatorChar.ToString())

        if (-not $endsWithSeparator) {
            break
        }

        $full = $full.Substring(0, $full.Length - 1)
    }

    return $full
}

function Test-PathWithinDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Candidate,

        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $candidateFull =
        Normalize-ComparisonPath -Path $Candidate
    $directoryFull =
        Normalize-ComparisonPath -Path $Directory

    if ($candidateFull.Equals(
            $directoryFull,
            [StringComparison]::OrdinalIgnoreCase))
    {
        return $true
    }

    $directoryPrefix = $directoryFull
    if (-not $directoryPrefix.EndsWith(
            [IO.Path]::DirectorySeparatorChar.ToString()))
    {
        $directoryPrefix +=
            [IO.Path]::DirectorySeparatorChar
    }

    return $candidateFull.StartsWith(
        $directoryPrefix,
        [StringComparison]::OrdinalIgnoreCase)
}

function Test-DirectoryOverlap {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Left,

        [Parameter(Mandatory = $true)]
        [string]$Right
    )

    $leftWithinRight =
        Test-PathWithinDirectory -Candidate $Left -Directory $Right
    $rightWithinLeft =
        Test-PathWithinDirectory -Candidate $Right -Directory $Left

    return $leftWithinRight -or $rightWithinLeft
}

if (Test-DirectoryOverlap -Left $dataRoot -Right $libraryPath) {
    throw "Acceptance data root '$dataRoot' overlaps representative library '$libraryPath'. Choose an OutputDirectory outside the source library."
}

foreach ($resultPath in @(
    $coldResultPath,
    $warmResultPath,
    $summaryPath
)) {
    if (Test-PathWithinDirectory -Candidate $resultPath -Directory $libraryPath) {
        throw "Acceptance result path '$resultPath' is inside representative library '$libraryPath'. Choose an OutputDirectory outside the source library."
    }
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

foreach ($staleOutput in @(
    $coldResultPath,
    $warmResultPath,
    $summaryPath
)) {
    if (Test-Path -LiteralPath $staleOutput) {
        Remove-Item -LiteralPath $staleOutput -Force
    }
}

if (Test-Path -LiteralPath $dataRoot) {
    try {
        Remove-Item -LiteralPath $dataRoot -Recurse -Force
    }
    catch {
        throw "Unable to reset acceptance data root '$dataRoot'. Close any running Lumine.App process that may still own instance.lock, then retry. $($_.Exception.Message)"
    }
}

New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null

$oldHardware = $env:LUMINE_HARDWARE_ID
$oldRevision = $env:LUMINE_REVISION
$oldCompositionMode = $env:LUMINE_WIN32_COMPOSITION_MODE

if ([string]::IsNullOrWhiteSpace($HardwareId)) {
    $HardwareId = $env:COMPUTERNAME
}

if ([string]::IsNullOrWhiteSpace($Revision)) {
    $revisionManifest =
        Join-Path (Split-Path -Parent $exePath) "revision.txt"

    if (-not (Test-Path -LiteralPath $revisionManifest)) {
        throw "Acceptance build revision is unknown. The NativeAOT artifact must contain revision.txt next to Lumine.App.exe, or -Revision must be supplied explicitly."
    }

    $Revision =
        (Get-Content -LiteralPath $revisionManifest -Raw).Trim()
}

$revisionPattern = '^[0-9a-fA-F]{7,64}$'
if ([string]::IsNullOrWhiteSpace($Revision) -or $Revision -eq "unknown" -or $Revision -notmatch $revisionPattern) {
    throw "Acceptance build revision '$Revision' is not a valid Git commit revision."
}

$env:LUMINE_HARDWARE_ID = $HardwareId
$env:LUMINE_REVISION = $Revision

if ($Win32CompositionMode -eq "Default") {
    $env:LUMINE_WIN32_COMPOSITION_MODE = $null
}
else {
    $env:LUMINE_WIN32_COMPOSITION_MODE = $Win32CompositionMode
}

function Get-MetadataValue {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result,

        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    $property = $Result.metadata.PSObject.Properties[$Key]
    if ($null -eq $property) {
        return $null
    }

    return [string]$property.Value
}

function Get-MaxMeasurement {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $values = @(
        $Result.measurements |
            Where-Object { $_.name -eq $Name } |
            ForEach-Object { [double]$_.durationMs }
    )

    if ($values.Count -eq 0) {
        throw "Acceptance result did not contain measurement '$Name'."
    }

    return ($values | Measure-Object -Maximum).Maximum
}

function Get-MeasurementAfterWorkingSet {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $matches = @(
        $Result.measurements |
            Where-Object { $_.name -eq $Name }
    )

    if ($matches.Count -eq 0) {
        throw "Acceptance result did not contain measurement '$Name'."
    }

    return [int64]$matches[-1].after.workingSetBytes
}

function Get-Bottlenecks {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result
    )

    return @(
        $Result.measurements |
            Where-Object {
                $_.name -notin @(
                    "acceptance.long_browse",
                    "acceptance.idle_settle"
                )
            } |
            Sort-Object -Property durationMs -Descending |
            Select-Object -First 5 |
            ForEach-Object {
                [ordered]@{
                    name = $_.name
                    durationMs = [Math]::Round([double]$_.durationMs, 3)
                }
            }
    )
}

function ConvertTo-ProcessArgument {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') {
        return $Value
    }

    # ProcessStartInfo.Arguments uses the Windows command-line parser on
    # Windows PowerShell 5.1. Quote according to the CommandLineToArgvW /
    # CRT rules so paths containing spaces remain one argument.
    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    $backslashes = 0

    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') {
            $backslashes++
            continue
        }

        if ($character -eq '"') {
            if ($backslashes -gt 0) {
                [void]$builder.Append(('\' * ($backslashes * 2)))
                $backslashes = 0
            }

            [void]$builder.Append('\"')
            continue
        }

        if ($backslashes -gt 0) {
            [void]$builder.Append(('\' * $backslashes))
            $backslashes = 0
        }

        [void]$builder.Append($character)
    }

    if ($backslashes -gt 0) {
        [void]$builder.Append(('\' * ($backslashes * 2)))
    }

    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-AcceptanceProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $exePath
    $startInfo.UseShellExecute = $false
    $startInfo.WorkingDirectory = Split-Path -Parent $exePath
    $startInfo.Arguments = (
        $Arguments |
            ForEach-Object { ConvertTo-ProcessArgument -Value $_ }
    ) -join ' '

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Unable to start Lumine.App acceptance process."
    }

    try {
        $startedAtUtc = $process.StartTime.ToUniversalTime()
        $processId = $process.Id
        $process.WaitForExit()

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            ProcessId = $processId
            StartedAtUtc = $startedAtUtc
        }
    }
    finally {
        $process.Dispose()
    }
}

function Write-CrashDiagnostics {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Mode,

        [Parameter(Mandatory = $true)]
        [object]$ProcessResult
    )

    $diagnosticPath =
        Join-Path $outputRoot "$Mode-crash-diagnostics.txt"
    $lines =
        New-Object System.Collections.Generic.List[string]

    $lines.Add("mode=$Mode")
    $lines.Add("exitCode=$($ProcessResult.ExitCode)")
    $lines.Add("pid=$($ProcessResult.ProcessId)")
    $lines.Add("startedAtUtc=$($ProcessResult.StartedAtUtc.ToString('O'))")
    $lines.Add("revision=$Revision")
    $lines.Add("win32CompositionMode=$Win32CompositionMode")
    $lines.Add("os=$([Environment]::OSVersion.VersionString)")
    $lines.Add("is64BitProcess=$([Environment]::Is64BitProcess)")
    $lines.Add("is64BitOperatingSystem=$([Environment]::Is64BitOperatingSystem)")

    try {
        $controllers =
            Get-CimInstance Win32_VideoController -ErrorAction Stop

        foreach ($controller in @($controllers)) {
            $lines.Add(
                "gpu=$($controller.Name) | driver=$($controller.DriverVersion) | status=$($controller.Status)")
        }
    }
    catch {
        $lines.Add(
            "gpu-query-error=$($_.Exception.Message)")
    }

    $eventStart =
        $ProcessResult.StartedAtUtc.ToLocalTime().AddSeconds(-2)
    $matchingEvents = @()

    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        try {
            $matchingEvents = @(
                Get-WinEvent -FilterHashtable @{
                    LogName = "Application"
                    Id = @(1000, 1001)
                    StartTime = $eventStart
                } -ErrorAction Stop |
                    Where-Object {
                        $_.Message -match "Lumine\.App\.exe"
                    } |
                    Select-Object -First 6
            )
        }
        catch {
            $lines.Add(
                "event-log-query-error=$($_.Exception.Message)")
            break
        }

        if ($matchingEvents.Count -gt 0) {
            break
        }

        Start-Sleep -Milliseconds 250
    }

    foreach ($event in $matchingEvents) {
        $lines.Add("--- Windows Application event ---")
        $lines.Add("eventId=$($event.Id)")
        $lines.Add("eventTime=$($event.TimeCreated.ToString('O'))")
        $lines.Add(
            ($event.Message -replace "\r?\n", " | "))
    }

    if ($matchingEvents.Count -eq 0) {
        $lines.Add("windowsApplicationEvent=not-found")
    }

    $runtimeLog =
        Join-Path $dataRoot "runtime.log"
    if (Test-Path -LiteralPath $runtimeLog) {
        $lines.Add("--- Lumine runtime.log ---")
        $lines.Add(
            (Get-Content -LiteralPath $runtimeLog -Raw))
    }

    $lines |
        Set-Content -LiteralPath $diagnosticPath -Encoding UTF8

    return $diagnosticPath
}

function Invoke-CoreAcceptance {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Mode,

        [Parameter(Mandatory = $true)]
        [string]$ResultPath
    )

    Write-Host ""
    Write-Host "=== Lumine real-library acceptance: $Mode ==="
    Write-Host "Library: $libraryPath"
    Write-Host "Output : $ResultPath"

    $acceptanceArgs = @(
        "--core-acceptance",
        "--library-dir=$libraryPath",
        "--data-dir=$dataRoot",
        "--output=$ResultPath",
        "--mode=$Mode",
        "--min-assets=$MinimumAssets",
        "--browse-seconds=$BrowseSeconds",
        "--idle-seconds=$IdleSeconds"
    )

    if (Test-Path -LiteralPath $ResultPath) {
        Remove-Item -LiteralPath $ResultPath -Force
    }

    $processResult =
        Invoke-AcceptanceProcess -Arguments $acceptanceArgs
    $processExitCode = [int]$processResult.ExitCode

    if ($processExitCode -ne 0) {
        $failureDetails = @()
        $crashDiagnosticPath =
            Write-CrashDiagnostics -Mode $Mode -ProcessResult $processResult
        $failureDetails +=
            "crashDiagnostics=$crashDiagnosticPath"

        if (Test-Path -LiteralPath $ResultPath) {
            try {
                $failedResult =
                    Get-Content -LiteralPath $ResultPath -Raw |
                        ConvertFrom-Json

                $failureType =
                    Get-MetadataValue -Result $failedResult -Key "acceptance.failure_type"
                $failureMessage =
                    Get-MetadataValue -Result $failedResult -Key "acceptance.failure"
                $shutdownFailure =
                    Get-MetadataValue -Result $failedResult -Key "shutdown.failure"

                if (-not [string]::IsNullOrWhiteSpace($failureType)) {
                    $failureDetails += "type=$failureType"
                }

                if (-not [string]::IsNullOrWhiteSpace($failureMessage)) {
                    $failureDetails += "failure=$failureMessage"
                }

                if (-not [string]::IsNullOrWhiteSpace($shutdownFailure)) {
                    $failureDetails += "shutdown=$shutdownFailure"
                }
            }
            catch {
                $failureDetails += "result-json-read-error=$($_.Exception.Message)"
            }
        }

        $detailSuffix =
            if ($failureDetails.Count -gt 0) {
                " Details: " + ($failureDetails -join " | ")
            }
            else {
                ""
            }

        throw "Lumine.App acceptance process failed in $Mode mode with exit code $processExitCode.$detailSuffix"
    }

    if (-not (Test-Path -LiteralPath $ResultPath)) {
        throw "Acceptance process did not write $ResultPath."
    }

    $marker = Join-Path $dataRoot "runtime.unclean"
    if (Test-Path -LiteralPath $marker) {
        throw "Clean $Mode acceptance left runtime.unclean behind."
    }

    $wal = Join-Path $dataRoot "library.db-wal"
    if ((Test-Path -LiteralPath $wal) -and ((Get-Item -LiteralPath $wal).Length -ne 0)) {
        throw "Clean $Mode acceptance left a non-empty SQLite WAL."
    }

    $result =
        Get-Content -LiteralPath $ResultPath -Raw |
            ConvertFrom-Json

    $reportedMode =
        Get-MetadataValue -Result $result -Key "acceptance.mode"
    if ($reportedMode -ne $Mode) {
        throw "Acceptance result mode mismatch: expected '$Mode', got '$reportedMode'."
    }

    if ([int]$result.schemaVersion -ne 1) {
        throw "Acceptance result schema mismatch: expected 1, got '$($result.schemaVersion)'."
    }

    $reportedRevision =
        [string]$result.environment.appRevision
    if ($reportedRevision -ne $Revision) {
        throw "Acceptance result revision mismatch: expected '$Revision', got '$reportedRevision'."
    }

    if ([string]$result.environment.hardwareId -ne $HardwareId) {
        throw "Acceptance hardware ID mismatch: expected '$HardwareId', got '$($result.environment.hardwareId)'."
    }

    $processArchitecture =
        [string]$result.environment.processArchitecture
    $osArchitecture =
        [string]$result.environment.osArchitecture

    if ($processArchitecture -ne "X64" -or $osArchitecture -ne "X64") {
        throw "Acceptance must run as native Windows x64; process='$processArchitecture', OS='$osArchitecture'."
    }

    return $result
}

try {
    $cold = Invoke-CoreAcceptance -Mode "cold" -ResultPath $coldResultPath
    $warm = Invoke-CoreAcceptance -Mode "warm" -ResultPath $warmResultPath

    $coldAssetCount =
        [int64](Get-MetadataValue -Result $cold -Key "library.asset_count")
    $warmAssetCount =
        [int64](Get-MetadataValue -Result $warm -Key "library.asset_count")
    if ($coldAssetCount -ne $warmAssetCount) {
        throw "Representative library changed between cold and warm runs: cold=$coldAssetCount, warm=$warmAssetCount assets. Re-run while the source library is stable."
    }

    $coldLibraryHash =
        Get-MetadataValue -Result $cold -Key "library.path_sha256"
    $warmLibraryHash =
        Get-MetadataValue -Result $warm -Key "library.path_sha256"
    if ($coldLibraryHash -ne $warmLibraryHash) {
        throw "Cold and warm results do not refer to the same representative library path."
    }

    $coldCacheBeforeFiles =
        [int64](Get-MetadataValue -Result $cold -Key "thumbnail.cache_before.files")
    $coldCacheBeforeBytes =
        [int64](Get-MetadataValue -Result $cold -Key "thumbnail.cache_before.bytes")
    if ($coldCacheBeforeFiles -ne 0 -or $coldCacheBeforeBytes -ne 0) {
        throw "Cold acceptance did not start with an empty persistent thumbnail cache: files=$coldCacheBeforeFiles, bytes=$coldCacheBeforeBytes."
    }

    foreach ($pair in @(
        @{ Name = "cold"; Result = $cold },
        @{ Name = "warm"; Result = $warm }
    )) {
        $mode = $pair.Name
        $result = $pair.Result

        if ((Get-MetadataValue -Result $result -Key "acceptance.automated_result") -ne "pass") {
            throw "$mode automated acceptance did not report pass."
        }

        $assetCount = [int64](Get-MetadataValue -Result $result -Key "library.asset_count")
        if ($assetCount -lt $MinimumAssets) {
            throw "$mode acceptance indexed only $assetCount assets."
        }

        $tileFailures = [int64](Get-MetadataValue -Result $result -Key "viewer.tile_load_failures")
        if ($tileFailures -ne 0) {
            throw "$mode acceptance recorded $tileFailures visible tile failures."
        }

        $thumbnailFailures = [int64](Get-MetadataValue -Result $result -Key "thumbnail.failed")
        if ($thumbnailFailures -ne 0) {
            throw "$mode acceptance recorded $thumbnailFailures thumbnail failures."
        }

        $viewerRequestFailures = [int64](Get-MetadataValue -Result $result -Key "viewer.thumbnail_requests_failed")
        if ($viewerRequestFailures -ne 0) {
            throw "$mode acceptance recorded $viewerRequestFailures Viewer thumbnail request failure(s)."
        }

        $inFlightThumbnailRequests = [int64](Get-MetadataValue -Result $result -Key "viewer.in_flight_thumbnail_requests")
        if ($inFlightThumbnailRequests -ne 0) {
            throw "$mode acceptance still had $inFlightThumbnailRequests in-flight thumbnail request(s) after idle settle."
        }

        $activeBitmapDecodes = [int64](Get-MetadataValue -Result $result -Key "viewer.active_bitmap_decodes")
        if ($activeBitmapDecodes -ne 0) {
            throw "$mode acceptance still had $activeBitmapDecodes active bitmap decode(s) after idle settle."
        }

        $maintenanceFailures = [int64](Get-MetadataValue -Result $result -Key "thumbnail.maintenance_runs_failed")
        if ($maintenanceFailures -ne 0) {
            throw "$mode acceptance recorded $maintenanceFailures thumbnail cache maintenance failure(s)."
        }

        $filesystemQueueDepth = [int64](Get-MetadataValue -Result $result -Key "filesystem.queue_depth")
        if ($filesystemQueueDepth -ne 0) {
            throw "$mode acceptance still had filesystem queue depth $filesystemQueueDepth after idle settle."
        }

        $filesystemEventsObserved = [int64](Get-MetadataValue -Result $result -Key "filesystem.events_observed")
        if ($filesystemEventsObserved -ne 0) {
            throw "$mode representative source library changed during acceptance; watcher observed $filesystemEventsObserved event(s)."
        }

        $runtimeReconciliations = [int64](Get-MetadataValue -Result $result -Key "filesystem.reconciliations")
        if ($runtimeReconciliations -ne 0) {
            throw "$mode runtime filesystem synchronization performed $runtimeReconciliations reconciliation(s) on the stable representative library."
        }

        $interruptedWrites = [int64](Get-MetadataValue -Result $result -Key "thumbnail.cache_after.interrupted_writes")
        if ($interruptedWrites -ne 0) {
            throw "$mode acceptance left $interruptedWrites interrupted thumbnail writes."
        }

        $reconcileFailures = [int64](Get-MetadataValue -Result $result -Key "filesystem.reconcile_failures")
        if ($reconcileFailures -ne 0) {
            throw "$mode acceptance recorded $reconcileFailures filesystem reconcile failures."
        }

        $cacheBytes = [int64](Get-MetadataValue -Result $result -Key "thumbnail.cache_after.bytes")
        $cacheLimit = [int64](Get-MetadataValue -Result $result -Key "thumbnail.cache_configured_bytes")
        if ($cacheBytes -gt $cacheLimit) {
            throw "$mode thumbnail cache exceeded its configured disk budget before shutdown: $cacheBytes > $cacheLimit bytes."
        }

        $postShutdownCacheBytes = [int64](Get-MetadataValue -Result $result -Key "thumbnail.cache_post_shutdown.bytes")
        $postShutdownCacheLimit = [int64](Get-MetadataValue -Result $result -Key "thumbnail.cache_post_shutdown.configured_bytes")
        $postShutdownInterruptedWrites = [int64](Get-MetadataValue -Result $result -Key "thumbnail.cache_post_shutdown.interrupted_writes")

        if ($postShutdownCacheBytes -gt $postShutdownCacheLimit) {
            throw "$mode post-shutdown thumbnail cache exceeded its configured disk budget: $postShutdownCacheBytes > $postShutdownCacheLimit bytes."
        }

        if ($postShutdownInterruptedWrites -ne 0) {
            throw "$mode post-shutdown thumbnail cache retained $postShutdownInterruptedWrites interrupted write(s)."
        }

        $maxScroll = Get-MaxMeasurement -Result $result -Name "viewer.fast_scroll_refresh"
        if ($maxScroll -gt $MaxFastScrollMs) {
            throw "$mode fast-scroll refresh exceeded $MaxFastScrollMs ms: $([Math]::Round($maxScroll, 1)) ms."
        }
    }

    $warmSourceOpens = [int64](Get-MetadataValue -Result $warm -Key "thumbnail.source_opens")
    if ($warmSourceOpens -ne 0) {
        throw "Warm acceptance reopened original sources for thumbnail generation $warmSourceOpens time(s)."
    }

    $warmCacheHits = [int64](Get-MetadataValue -Result $warm -Key "thumbnail.cache_hits")
    if ($warmCacheHits -le 0) {
        throw "Warm acceptance did not observe persistent thumbnail cache hits."
    }

    $warmBootstrap = Get-MetadataValue -Result $warm -Key "filesystem.bootstrap_mode"
    $warnings = @()

    if ($warmBootstrap -ne "UsnDelta") {
        $warnings += "Warm filesystem bootstrap was '$warmBootstrap' rather than 'UsnDelta'. This can be valid when USN journal replay is unavailable, but should be reviewed for the target library volume."
    }

    $coldMaxScroll = Get-MaxMeasurement -Result $cold -Name "viewer.fast_scroll_refresh"
    $warmMaxScroll = Get-MaxMeasurement -Result $warm -Name "viewer.fast_scroll_refresh"

    $coldRawSha256 =
        (Get-FileHash -LiteralPath $coldResultPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $warmRawSha256 =
        (Get-FileHash -LiteralPath $warmResultPath -Algorithm SHA256).Hash.ToLowerInvariant()

    $summary = [ordered]@{
        schemaVersion = 1
        automatedDecision = "pass"
        hardwareId = $HardwareId
        appRevision = $Revision
        win32CompositionMode = $Win32CompositionMode
        libraryPathSha256 = (Get-MetadataValue -Result $warm -Key "library.path_sha256")
        assetCount = [int64](Get-MetadataValue -Result $warm -Key "library.asset_count")
        acceptanceCriteria = [ordered]@{
            minimumAssets = $MinimumAssets
            browseSecondsPerRun = $BrowseSeconds
            idleSecondsPerRun = $IdleSeconds
            maxFastScrollMs = $MaxFastScrollMs
        }
        rawResultSha256 = [ordered]@{
            cold = $coldRawSha256
            warm = $warmRawSha256
        }
        cold = [ordered]@{
            maxFastScrollMs = [Math]::Round($coldMaxScroll, 3)
            peakWorkingSetBytes = [int64](Get-MetadataValue -Result $cold -Key "resource.peak_working_set_bytes")
            idleWorkingSetBytes = Get-MeasurementAfterWorkingSet -Result $cold -Name "acceptance.idle_settle"
            thumbnailSourceOpens = [int64](Get-MetadataValue -Result $cold -Key "thumbnail.source_opens")
            thumbnailCacheHits = [int64](Get-MetadataValue -Result $cold -Key "thumbnail.cache_hits")
            thumbnailCacheBytes = [int64](Get-MetadataValue -Result $cold -Key "thumbnail.cache_after.bytes")
            thumbnailCacheLimitBytes = [int64](Get-MetadataValue -Result $cold -Key "thumbnail.cache_configured_bytes")
            postShutdownThumbnailCacheBytes = [int64](Get-MetadataValue -Result $cold -Key "thumbnail.cache_post_shutdown.bytes")
            filesystemBootstrapMode = (Get-MetadataValue -Result $cold -Key "filesystem.bootstrap_mode")
        }
        warm = [ordered]@{
            maxFastScrollMs = [Math]::Round($warmMaxScroll, 3)
            peakWorkingSetBytes = [int64](Get-MetadataValue -Result $warm -Key "resource.peak_working_set_bytes")
            idleWorkingSetBytes = Get-MeasurementAfterWorkingSet -Result $warm -Name "acceptance.idle_settle"
            thumbnailSourceOpens = $warmSourceOpens
            thumbnailCacheHits = $warmCacheHits
            thumbnailCacheBytes = [int64](Get-MetadataValue -Result $warm -Key "thumbnail.cache_after.bytes")
            thumbnailCacheLimitBytes = [int64](Get-MetadataValue -Result $warm -Key "thumbnail.cache_configured_bytes")
            postShutdownThumbnailCacheBytes = [int64](Get-MetadataValue -Result $warm -Key "thumbnail.cache_post_shutdown.bytes")
            filesystemBootstrapMode = $warmBootstrap
        }
        measuredBottlenecks = [ordered]@{
            cold = @(Get-Bottlenecks -Result $cold)
            warm = @(Get-Bottlenecks -Result $warm)
        }
        knownLimitations = @(
            "Per-process OS disk-read byte counters are not collected; Core-owned thumbnail source-open and metadata-hash counters are recorded instead.",
            "Dedicated GPU-memory usage is not collected because the Core acceptance path owns no AI/GPU model runtime; compositor residency remains platform-owned.",
            "Automated timing/correctness cannot decide subjective visual comfort; the visible run still requires the manual observation checklist."
        )
        additionalFixIssues = @()
        acceptanceDecision = "automated-pass-manual-observation-required"
        warnings = $warnings
        manualObservation = [ordered]@{
            requiredBeforeClosingIssue295 = $true
            check = @(
                "Initial grid appears without an unpleasant stall.",
                "Continuous scroll and direction reversal remain visually responsive.",
                "Detail next/previous/zoom/1:1 interactions remain comfortable.",
                "No visible periodic whole-library stall occurs during the browse/idle window.",
                "No unresolved P0/P1 Core behavior is observed."
            )
        }
        rawResults = [ordered]@{
            cold = $coldResultPath
            warm = $warmResultPath
        }
    }

    $summary |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $summaryPath -Encoding UTF8

    Write-Host ""
    Write-Host "Automated real-library acceptance passed."
    Write-Host "Assets              : $($summary.assetCount)"
    Write-Host "Cold max fast-scroll: $($summary.cold.maxFastScrollMs) ms"
    Write-Host "Warm max fast-scroll: $($summary.warm.maxFastScrollMs) ms"
    Write-Host "Warm source opens    : $warmSourceOpens"
    Write-Host "Warm cache hits      : $warmCacheHits"
    Write-Host "Summary              : $summaryPath"

    if ($warnings.Count -gt 0) {
        Write-Warning ($warnings -join [Environment]::NewLine)
    }

    Write-Host ""
    Write-Host "Observe the visible Lumine windows during the run. #295 remains open until the manual usability observations in summary.json are confirmed."
}
finally {
    $env:LUMINE_HARDWARE_ID = $oldHardware
    $env:LUMINE_REVISION = $oldRevision
    $env:LUMINE_WIN32_COMPOSITION_MODE = $oldCompositionMode
}
