# tools/migrate-relics-to-versioned.ps1
# Phase 10.5.L1: migrate src/Core/Data/Relics/*.json from flat to versioned schema.
# Idempotent: files already containing a "versions" key are skipped.
#
# Run: pwsh tools/migrate-relics-to-versioned.ps1 (PowerShell 7 recommended)
#      Also works on Windows PowerShell 5.1 (uses PSCustomObject path, not -AsHashtable).

$ErrorActionPreference = 'Stop'

$relicsDir = Join-Path $PSScriptRoot '..\src\Core\Data\Relics'
$now = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

function Get-PropertyNames {
    param($obj)
    if ($null -eq $obj) { return @() }
    return @($obj.PSObject.Properties | ForEach-Object { $_.Name })
}

$migrated = 0
$skipped = 0

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

Get-ChildItem $relicsDir -Filter '*.json' | ForEach-Object {
    # Read as UTF-8 explicitly. Get-Content -Raw on PS 5.1 uses system locale by default,
    # which mangles Japanese strings on Windows-JP shells.
    $raw = [System.IO.File]::ReadAllText($_.FullName, $utf8NoBom)
    $obj = $raw | ConvertFrom-Json

    $names = Get-PropertyNames $obj

    if ($names -contains 'versions') {
        Write-Host "Skipping (already versioned): $($_.Name)"
        $script:skipped++
        return
    }

    # spec = root minus id/name/displayName, keeping insertion order.
    $spec = [ordered]@{}
    foreach ($key in $names) {
        if ($key -eq 'id' -or $key -eq 'name' -or $key -eq 'displayName') { continue }
        $spec[$key] = $obj.$key
    }

    $displayNameValue = $null
    if ($names -contains 'displayName') { $displayNameValue = $obj.displayName }

    $versionEntry = [ordered]@{
        version   = 'v1'
        createdAt = $now
        label     = 'original'
        spec      = $spec
    }

    $versioned = [ordered]@{
        id            = $obj.id
        name          = $obj.name
        displayName   = $displayNameValue
        activeVersion = 'v1'
        versions      = @($versionEntry)
    }

    $output = $versioned | ConvertTo-Json -Depth 32

    # UTF-8 without BOM
    [System.IO.File]::WriteAllText($_.FullName, $output, $utf8NoBom)

    Write-Host "Migrated: $($_.Name)"
    $script:migrated++
}

Write-Host ''
Write-Host "Done. migrated=$migrated, skipped=$skipped"
