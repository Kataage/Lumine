param(
    [string]$BenchmarkPath = "artifacts/benchmarks/full-resolution-access-policy.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BenchmarkPath)) {
    throw "Missing full-resolution access-policy benchmark: $BenchmarkPath"
}

$result = Get-Content $BenchmarkPath -Raw | ConvertFrom-Json

function Get-Metadata([string]$Name) {
    $property = $result.metadata.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "Missing access-policy metadata '$Name'."
    }

    return [string]$property.Value
}

function Require-PolicyCase(
    [string]$Case,
    [string]$Policy,
    [bool]$MustSucceed
) {
    $prefix = "case.$Case.$Policy"
    $success = [bool]::Parse((Get-Metadata "$prefix.success"))

    if ($MustSucceed -and -not $success) {
        throw "$Case/$Policy failed: $(Get-Metadata "$prefix.error")"
    }

    if ($success) {
        $decodeMs = [double](Get-Metadata "$prefix.decode_ms")
        $peak = [long](Get-Metadata "$prefix.peak_working_set_bytes")
        $vipsMem = [long](Get-Metadata "$prefix.vips_peak_tracked_bytes")
        $allocated = [long](Get-Metadata "$prefix.managed_allocated_bytes")

        if ($decodeMs -le 0) {
            throw "$Case/$Policy reported non-positive decode latency."
        }

        if ($peak -le 0) {
            throw "$Case/$Policy reported no working-set sample."
        }

        if ($vipsMem -lt 0 -or $allocated -lt 0) {
            throw "$Case/$Policy reported invalid resource accounting."
        }
    }

    return $success
}

$mandatory = @(
    "jpeg",
    "jpeg-icc",
    "jpeg-oriented",
    "jpeg-icc-oriented",
    "png-alpha",
    "webp",
    "tiff",
    "png-large-backing"
)

foreach ($case in $mandatory) {
    $randomOk = Require-PolicyCase $case "random" $true
    $sequentialOk = Require-PolicyCase $case "sequential" $false

    if ($randomOk -and $sequentialOk) {
        $randomChecksum = [long](Get-Metadata "case.$case.random.checksum")
        $sequentialChecksum = [long](Get-Metadata "case.$case.sequential.checksum")

        if ($randomChecksum -ne $sequentialChecksum) {
            throw "$case decoded different pixels between Random and Sequential."
        }
    }
}

foreach ($optional in @("avif", "heic")) {
    $capability = [bool]::Parse((Get-Metadata "capability.$optional"))
    if ($capability) {
        $null = Require-PolicyCase $optional "random" $true
        $null = Require-PolicyCase $optional "sequential" $false
    }
}

$randomCancellation = [double](Get-Metadata "cancellation.random_ms")
if ($randomCancellation -gt 1500) {
    throw "Random cancellation latency exceeded 1.5 seconds: $randomCancellation ms"
}

$sequentialCancellationText = Get-Metadata "cancellation.sequential_ms"
if (-not [string]::IsNullOrWhiteSpace($sequentialCancellationText)) {
    $sequentialCancellation = [double]$sequentialCancellationText
    if ($sequentialCancellation -gt 1500) {
        throw "Sequential cancellation latency exceeded 1.5 seconds: $sequentialCancellation ms"
    }
}

$production = Get-Metadata "production_policy"
if ($production -ne "adaptive") {
    throw "Production full-resolution access policy must be adaptive after #310 measurement; got '$production'."
}

$expectedProductionPolicies = @{
    "jpeg" = "random"
    "jpeg-icc" = "random"
    "jpeg-oriented" = "random"
    "jpeg-icc-oriented" = "random"
    "png-alpha" = "sequential"
    "webp" = "random"
    "tiff" = "random"
    "png-large-backing" = "sequential"
}

foreach ($entry in $expectedProductionPolicies.GetEnumerator()) {
    $actual = Get-Metadata "case.$($entry.Key).production_policy"
    if ($actual -ne $entry.Value) {
        throw "Adaptive production policy for $($entry.Key) was '$actual'; expected '$($entry.Value)'."
    }
}

foreach ($optional in @("avif", "heic")) {
    $capability = [bool]::Parse((Get-Metadata "capability.$optional"))
    if ($capability) {
        $actual = Get-Metadata "case.$optional.production_policy"
        if ($actual -ne "random") {
            throw "Adaptive production policy for $optional was '$actual'; expected 'random' until #312 validates the HEIF/HEIC production contract."
        }
    }
}

$largeRandomTemp = [long](Get-Metadata "case.png-large-backing.random.temp_peak_bytes")
$largeSequentialTemp = [long](Get-Metadata "case.png-large-backing.sequential.temp_peak_bytes")
$largeRandomMs = [double](Get-Metadata "case.png-large-backing.random.decode_ms")
$largeSequentialMs = [double](Get-Metadata "case.png-large-backing.sequential.decode_ms")

if ($largeSequentialTemp -gt 4MB) {
    throw "Sequential large-PNG decode created unexpected temporary backing: $largeSequentialTemp bytes"
}

if ($largeRandomTemp -gt 32MB -and $largeSequentialTemp -ge $largeRandomTemp) {
    throw "Sequential did not eliminate the large-PNG temporary backing observed with Random."
}

if ($largeSequentialMs -gt ($largeRandomMs * 1.25)) {
    throw "Sequential large-PNG decode regressed beyond the accepted 25% guard: random=$largeRandomMs ms sequential=$largeSequentialMs ms"
}

Write-Host "Full-resolution access-policy adaptive benchmark passed."
foreach ($case in $mandatory) {
    $randomMs = [double](Get-Metadata "case.$case.random.decode_ms")
    $seqOk = [bool]::Parse((Get-Metadata "case.$case.sequential.success"))
    if ($seqOk) {
        $seqMs = [double](Get-Metadata "case.$case.sequential.decode_ms")
        $randomMem = [long](Get-Metadata "case.$case.random.peak_additional_working_set_bytes")
        $seqMem = [long](Get-Metadata "case.$case.sequential.peak_additional_working_set_bytes")
        $randomTemp = [long](Get-Metadata "case.$case.random.temp_peak_bytes")
        $seqTemp = [long](Get-Metadata "case.$case.sequential.temp_peak_bytes")
        Write-Host ("{0}: Random={1:N1} ms / +{2:N1} MiB / temp={3:N1} MiB; Sequential={4:N1} ms / +{5:N1} MiB / temp={6:N1} MiB" -f $case, $randomMs, ($randomMem / 1MB), ($randomTemp / 1MB), $seqMs, ($seqMem / 1MB), ($seqTemp / 1MB))
    }
    else {
        Write-Host ("{0}: Random={1:N1} ms; Sequential rejected: {2}" -f $case, $randomMs, (Get-Metadata "case.$case.sequential.error"))
    }
}
