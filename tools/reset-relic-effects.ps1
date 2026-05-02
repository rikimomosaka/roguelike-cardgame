# Phase 10.5.L1.5: 36 relic JSON の versioned 形式の各 version.spec で
#   - effects = []
#   - top-level "trigger" を削除 (versioned spec 内に存在すれば)
# にリセットするスクリプト。description / name / rarity / implemented 等は保持する。
#
# 使用例:
#   pwsh ./tools/reset-relic-effects.ps1
#
# UTF-8 BOM 無しで上書き。日本語 description はそのまま保持される。

$ErrorActionPreference = 'Stop'
$relicsDir = Join-Path $PSScriptRoot '..\src\Core\Data\Relics'

if (-not (Test-Path $relicsDir)) {
    throw "Relics directory not found: $relicsDir"
}

$utf8 = [System.Text.UTF8Encoding]::new($false)

Get-ChildItem $relicsDir -Filter '*.json' | ForEach-Object {
    $path = $_.FullName
    $json = [System.IO.File]::ReadAllText($path, $utf8)
    $obj = $json | ConvertFrom-Json

    if ($obj.versions) {
        foreach ($v in $obj.versions) {
            if ($v.spec) {
                # effects を空配列にリセット
                if ($v.spec.PSObject.Properties['effects']) {
                    $v.spec.effects = @()
                } else {
                    $v.spec | Add-Member -NotePropertyName 'effects' -NotePropertyValue @() -Force
                }
                # top-level trigger を削除 (versioned spec 内に存在すれば)
                if ($v.spec.PSObject.Properties['trigger']) {
                    $v.spec.PSObject.Properties.Remove('trigger')
                }
            }
        }
    }

    $output = $obj | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText($path, $output, $utf8)
    Write-Host "Reset: $($_.Name)"
}

Write-Host "Done. Reset $((Get-ChildItem $relicsDir -Filter '*.json').Count) relic files."
