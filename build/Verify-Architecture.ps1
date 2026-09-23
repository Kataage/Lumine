Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

foreach ($path in @("go.mod", "go.sum", "wails.json", "frontend", "internal")) {
    if (Test-Path (Join-Path $root $path)) {
        throw "Legacy production path must not exist in Lumine v2: $path"
    }
}

function Read-Project([string] $relativePath) {
    Get-Content -Raw (Join-Path $root $relativePath)
}

$core = Read-Project "src/Lumine.Core/Lumine.Core.csproj"
$library = Read-Project "src/Lumine.Library/Lumine.Library.csproj"
$image = Read-Project "src/Lumine.Image/Lumine.Image.csproj"
$viewer = Read-Project "src/Lumine.Viewer/Lumine.Viewer.csproj"
$app = Read-Project "src/Lumine.App/Lumine.App.csproj"

if ($core -match "ProjectReference") {
    throw "Lumine.Core must not reference another Lumine project."
}

foreach ($entry in @(
    @{ Name = "Library"; Text = $library },
    @{ Name = "Image"; Text = $image },
    @{ Name = "Viewer"; Text = $viewer }
)) {
    if ($entry.Text -notmatch "Lumine.Core") {
        throw "Lumine.$($entry.Name) must reference Lumine.Core."
    }

    foreach ($forbidden in @("Lumine.Library", "Lumine.Image", "Lumine.Viewer", "Lumine.App")) {
        if ($forbidden -ne "Lumine.$($entry.Name)" -and $entry.Text -match [regex]::Escape($forbidden)) {
            throw "Lumine.$($entry.Name) must not reference sibling/app project $forbidden."
        }
    }
}

foreach ($required in @("Lumine.Core", "Lumine.Library", "Lumine.Image", "Lumine.Viewer")) {
    if ($app -notmatch [regex]::Escape($required)) {
        throw "Lumine.App must compose $required."
    }
}

Write-Host "Lumine v2 architecture boundary verification passed."
