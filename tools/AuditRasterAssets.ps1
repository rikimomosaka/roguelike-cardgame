param(
    [int]$MinimumTransparentGutter = 8,
    [int]$MaximumFlatTopRun = 32
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not ('RcgRasterAlphaAudit' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class RcgRasterAlphaAudit
{
    public sealed class Report
    {
        public int VisiblePixels;
        public int BoundaryPixels;
        public int LightMatteCandidates;
        public int DarkCompositeCandidates;
        public int UnsanitizedTransparentPixels;
        public int GreenKeyCandidates;
        public int MagentaKeyCandidates;
        public int SmallNeutralBoundaryIslandPixels;
    }

    private static bool TouchesTransparent(byte[] bytes, int width, int height, int stride, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height) return true;
            if (bytes[ny * stride + nx * 4 + 3] <= 8) return true;
        }
        return false;
    }

    private static int Composite(byte foreground, byte alpha, int background)
    {
        return (foreground * alpha + background * (255 - alpha) + 127) / 255;
    }

    public static Report Inspect(Bitmap source, int left, int frameWidth)
    {
        using (var bitmap = new Bitmap(frameWidth, source.Height, PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.Clear(Color.Transparent);
                graphics.DrawImageUnscaled(source, -left, 0);
            }

            var result = new Report();
            var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var stride = Math.Abs(data.Stride);
                var bytes = new byte[stride * bitmap.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                var neutralBoundaryCandidates = new bool[bitmap.Width * bitmap.Height];
                for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var offset = y * stride + x * 4;
                    var b = bytes[offset];
                    var g = bytes[offset + 1];
                    var r = bytes[offset + 2];
                    var a = bytes[offset + 3];
                    if (a <= 8)
                    {
                        if (r != 0 || g != 0 || b != 0) result.UnsanitizedTransparentPixels++;
                        continue;
                    }
                    result.VisiblePixels++;
                    var other = Math.Max(r, b);
                    if (g >= 80 && g - other >= 28 && g >= other * 1.18)
                        result.GreenKeyCandidates++;
                    var magentaFloor = Math.Min(r, b);
                    if (magentaFloor >= 85 && magentaFloor - g >= 32 && Math.Abs(r - b) <= 96)
                        result.MagentaKeyCandidates++;

                    if (!TouchesTransparent(bytes, bitmap.Width, bitmap.Height, stride, x, y)) continue;
                    result.BoundaryPixels++;
                    var boundaryMinimum = Math.Min(r, Math.Min(g, b));
                    var boundaryMaximum = Math.Max(r, Math.Max(g, b));
                    var boundaryLuminance = (r * 54 + g * 183 + b * 19) / 256;
                    if (boundaryMaximum - boundaryMinimum <= 16 &&
                        boundaryLuminance >= 35 && boundaryLuminance <= 205)
                        neutralBoundaryCandidates[y * bitmap.Width + x] = true;

                    var bestOffset = -1;
                    var bestDistance = int.MaxValue;
                    const int radius = 4;
                    for (var dy = -radius; dy <= radius; dy++)
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        var squared = dx * dx + dy * dy;
                        if (squared == 0 || squared > radius * radius || squared >= bestDistance ||
                            nx < 1 || ny < 1 || nx >= bitmap.Width - 1 || ny >= bitmap.Height - 1)
                            continue;
                        var candidateOffset = ny * stride + nx * 4;
                        if (bytes[candidateOffset + 3] < 220 ||
                            TouchesTransparent(bytes, bitmap.Width, bitmap.Height, stride, nx, ny))
                            continue;
                        bestOffset = candidateOffset;
                        bestDistance = squared;
                    }
                    if (bestOffset < 0) continue;

                    var minimum = Math.Min(r, Math.Min(g, b));
                    var maximum = Math.Max(r, Math.Max(g, b));
                    var luminance = (r * 54 + g * 183 + b * 19) / 256;
                    var interiorR = bytes[bestOffset + 2];
                    var interiorG = bytes[bestOffset + 1];
                    var interiorB = bytes[bestOffset];
                    var interiorMinimum = Math.Min(interiorR, Math.Min(interiorG, interiorB));
                    var interiorMaximum = Math.Max(interiorR, Math.Max(interiorG, interiorB));
                    var interiorLuminance = (interiorR * 54 + interiorG * 183 + interiorB * 19) / 256;
                    var lightMatteCandidate = minimum >= 166 && maximum - minimum <= 44 &&
                        luminance >= interiorLuminance + 38 &&
                        (interiorMaximum - interiorMinimum >= maximum - minimum + 18 || interiorLuminance < 142);
                    if (lightMatteCandidate)
                        result.LightMatteCandidates++;

                    // The same edge as it will appear over the battle's near-
                    // black backdrop. A neutral bright line that rises sharply
                    // above the local interior is the visible halo being guarded.
                    var compositeR = Composite(r, a, 12);
                    var compositeG = Composite(g, a, 15);
                    var compositeB = Composite(b, a, 19);
                    var compositeMinimum = Math.Min(compositeR, Math.Min(compositeG, compositeB));
                    var compositeMaximum = Math.Max(compositeR, Math.Max(compositeG, compositeB));
                    var compositeLuminance = (compositeR * 54 + compositeG * 183 + compositeB * 19) / 256;
                    if (lightMatteCandidate &&
                        compositeMinimum >= 42 && compositeMaximum - compositeMinimum <= 42 &&
                        compositeLuminance >= interiorLuminance + 30)
                        result.DarkCompositeCandidates++;
                }

                var neutralVisited = new bool[neutralBoundaryCandidates.Length];
                for (var start = 0; start < neutralBoundaryCandidates.Length; start++)
                {
                    if (!neutralBoundaryCandidates[start] || neutralVisited[start]) continue;
                    var component = new System.Collections.Generic.List<int>();
                    var queue = new System.Collections.Generic.Queue<int>();
                    neutralVisited[start] = true;
                    queue.Enqueue(start);
                    while (queue.Count > 0)
                    {
                        var pixel = queue.Dequeue();
                        component.Add(pixel);
                        var x = pixel % bitmap.Width;
                        var y = pixel / bitmap.Width;
                        for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            var nx = x + dx;
                            var ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= bitmap.Width || ny >= bitmap.Height) continue;
                            var next = ny * bitmap.Width + nx;
                            if (!neutralBoundaryCandidates[next] || neutralVisited[next]) continue;
                            neutralVisited[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                    if (component.Count <= 20)
                        result.SmallNeutralBoundaryIslandPixels += component.Count;
                }
            }
            finally { bitmap.UnlockBits(data); }
            return result;
        }
    }
}
'@
}

$workspace = Split-Path -Parent $PSScriptRoot
$maximumFlatTopRunByFile = @{
    # These actors were previously sliced at a contact-sheet cell boundary.
    # Keep asset-specific approved silhouette limits so that the old 18-32px
    # cuts are rejected even though they are below the generic 32px guard.
    'goblin_c.png'       = 6
    'hobgoblin.png'      = 4
    'cultist_dark.png'   = 5
    'mushroom_a.png'     = 20
    'mushroom_b.png'     = 18
    'orc_red.png'        = 17
    'orc_blue.png'       = 10
    'ogre.png'           = 18
    'iron_golem_a.png'   = 16
    'guardian_golem.png' = 8
    'iron_golem_b.png'   = 6
    'iron_golem_c.png'   = 15
    'six_ghost.png'      = 13
    'act3_elite.png'     = 9
    'act3_boss.png'      = 4
}
$heroIds = @('default', 'guardian_mireille', 'tactician_yuno', 'enhancer_lyra')
$portraitKeyMode = @{
    'default.png' = 'green'
    'guardian_mireille.png' = 'green'
    'enhancer_lyra.png' = 'green'
    'tactician_yuno.png' = 'magenta'
}
$heroMotionSpecs = @{
    'stand' = @{ Width=192;  Height=256; Frames=1; FrameWidth=192 }
    'idle' = @{ Width=1536; Height=256; Frames=8; FrameWidth=192 }
    'attack' = @{ Width=1536; Height=256; Frames=4; FrameWidth=384 }
    'hurt' = @{ Width=768; Height=256; Frames=4; FrameWidth=192 }
}
$files = @()
foreach ($heroId in $heroIds) {
    $files += Get-ChildItem -LiteralPath (Join-Path $workspace 'src/Client/public/characters') `
        -Filter "${heroId}_*.png"
}
$portraitFiles = $heroIds | ForEach-Object {
    Get-Item -LiteralPath (Join-Path $workspace "src/Client/public/characters/portraits/$_.png")
}
$files += $portraitFiles
$files += Get-ChildItem -LiteralPath (Join-Path $workspace 'src/Client/public/characters/enemies') -Filter '*.png'
$files += Get-ChildItem -LiteralPath (Join-Path $workspace 'src/Client/public/characters/units') -Filter '*.png'
$files += Get-ChildItem -LiteralPath (Join-Path $workspace 'src/Client/public/icons/potions') -Filter '*.png'

$failures = @()
$referenceHeights = @{}
foreach ($file in ($files | Sort-Object FullName -Unique)) {
    $bitmap = [System.Drawing.Bitmap]::new($file.FullName)
    try {
        $heroMatch = [regex]::Match(
            $file.Name,
            '^(default|guardian_mireille|tactician_yuno|enhancer_lyra)_(stand|idle|attack|hurt)\.png$')
        if ($heroMatch.Success) {
            $motion = $heroMatch.Groups[2].Value
            $spec = $heroMotionSpecs[$motion]
            if ($bitmap.Width -ne $spec.Width -or $bitmap.Height -ne $spec.Height) {
                $failures += "$($file.FullName.Substring($workspace.Length + 1)) expected=$($spec.Width)x$($spec.Height) actual=$($bitmap.Width)x$($bitmap.Height)"
            }
        }
        if ($file.Directory.Name -eq 'portraits' -and ($bitmap.Width -ne 384 -or $bitmap.Height -ne 512)) {
            $failures += "$($file.FullName.Substring($workspace.Length + 1)) expected=384x512 actual=$($bitmap.Width)x$($bitmap.Height)"
        }

        $frameWidth = if ($heroMatch.Success) {
            $heroMotionSpecs[$heroMatch.Groups[2].Value].FrameWidth
        } elseif (
            $file.Directory.Name -eq 'characters' `
            -and $bitmap.Height -eq 256 `
            -and ($bitmap.Width % 192) -eq 0
        ) { 192 } else { $bitmap.Width }
        $frameCount = [int]($bitmap.Width / $frameWidth)

        for ($frame = 0; $frame -lt $frameCount; $frame++) {
            $minX = $frameWidth
            $minY = $bitmap.Height
            $maxX = -1
            $maxY = -1
            $edgePixels = 0
            for ($y = 0; $y -lt $bitmap.Height; $y++) {
                for ($x = 0; $x -lt $frameWidth; $x++) {
                    $alpha = $bitmap.GetPixel(($frame * $frameWidth) + $x, $y).A
                    if ($alpha -le 0) { continue }
                    if ($x -eq 0 -or $x -eq ($frameWidth - 1) -or $y -eq 0 -or $y -eq ($bitmap.Height - 1)) {
                        $edgePixels++
                    }
                    if ($alpha -le 8) { continue }
                    if ($x -lt $minX) { $minX = $x }
                    if ($x -gt $maxX) { $maxX = $x }
                    if ($y -lt $minY) { $minY = $y }
                    if ($y -gt $maxY) { $maxY = $y }
                }
            }

            $relative = $file.FullName.Substring($workspace.Length + 1)
            if ($maxX -lt 0) {
                $failures += "$relative frame $frame is fully transparent"
                continue
            }
            $gutters = @($minX, $minY, ($frameWidth - 1 - $maxX), ($bitmap.Height - 1 - $maxY))
            $minimum = ($gutters | Measure-Object -Minimum).Minimum
            if ($edgePixels -gt 0 -or $minimum -lt $MinimumTransparentGutter) {
                $failures += "$relative frame $frame edgePixels=$edgePixels gutters(L,T,R,B)=$($gutters -join ',')"
            }

            $alphaAudit = [RcgRasterAlphaAudit]::Inspect($bitmap, $frame * $frameWidth, $frameWidth)
            if ($alphaAudit.UnsanitizedTransparentPixels -gt 0) {
                $failures += "$relative frame $frame has $($alphaAudit.UnsanitizedTransparentPixels) A<=8 pixels with non-zero RGB"
            }
            $boundaryDenominator = [Math]::Max(1, $alphaAudit.BoundaryPixels)
            $lightHaloRatio = $alphaAudit.LightMatteCandidates / [double]$boundaryDenominator
            $darkHaloRatio = $alphaAudit.DarkCompositeCandidates / [double]$boundaryDenominator
            if ($lightHaloRatio -gt 0.03) {
                $failures += "$relative frame $frame lightMatteHalo=$($alphaAudit.LightMatteCandidates)/$($alphaAudit.BoundaryPixels) ($($lightHaloRatio.ToString('P2'))) limit=3%"
            }
            if ($darkHaloRatio -gt 0.03) {
                $failures += "$relative frame $frame darkCompositeHalo=$($alphaAudit.DarkCompositeCandidates)/$($alphaAudit.BoundaryPixels) ($($darkHaloRatio.ToString('P2'))) limit=3%"
            }

            if ($file.Directory.Name -eq 'portraits') {
                $keyCandidates = if ($portraitKeyMode[$file.Name] -eq 'magenta') {
                    $alphaAudit.MagentaKeyCandidates
                } else {
                    $alphaAudit.GreenKeyCandidates
                }
                $keyRatio = $keyCandidates / [double][Math]::Max(1, $alphaAudit.VisiblePixels)
                if ($keyRatio -gt 0.001) {
                    $failures += "$relative retained $($portraitKeyMode[$file.Name]) key pixels=$keyCandidates/$($alphaAudit.VisiblePixels) ($($keyRatio.ToString('P3'))) limit=0.1%"
                }
            }

            if ($heroMatch.Success) {
                $heroId = $heroMatch.Groups[1].Value
                $motion = $heroMatch.Groups[2].Value
                if ($frame -eq 0 -and $motion -in @('idle', 'attack', 'hurt')) {
                    if (-not $referenceHeights.ContainsKey($heroId)) { $referenceHeights[$heroId] = @{} }
                    $referenceHeights[$heroId][$motion] = ($maxY - $minY + 1)
                }
            }

            # A fixed-grid source crop can sever a head/weapon and still pass
            # the outer-gutter check after the cropped result is re-padded.
            # Catch the resulting unnaturally long, perfectly flat silhouette
            # at the first occupied row. Natural antialiased curves start with
            # a short run; the previously broken ogre/golems measured 80-142px.
            if ($file.Directory.Name -in @('characters', 'enemies')) {
                $longestTopRun = 0
                $currentTopRun = 0
                for ($x = 0; $x -lt $frameWidth; $x++) {
                    $alpha = $bitmap.GetPixel(($frame * $frameWidth) + $x, $minY).A
                    if ($alpha -gt 8) {
                        $currentTopRun++
                        if ($currentTopRun -gt $longestTopRun) { $longestTopRun = $currentTopRun }
                    } else {
                        $currentTopRun = 0
                    }
                }
                $flatTopLimit = if ($heroMatch.Success -and $heroMatch.Groups[2].Value -eq 'attack') {
                    # A horizontal blade, bolt or magic trail is valid in the
                    # deliberately wide 384px attack frame. Source-crop damage is
                    # still guarded by transparent gutters and the 72px ceiling.
                    72
                } elseif ($maximumFlatTopRunByFile.ContainsKey($file.Name)) {
                    $maximumFlatTopRunByFile[$file.Name]
                } else {
                    $MaximumFlatTopRun
                }
                if ($longestTopRun -gt $flatTopLimit) {
                    $failures += "$relative frame $frame suspiciousFlatTop=${longestTopRun}px limit=${flatTopLimit}px"
                }
                if ($heroMatch.Success -and $heroMatch.Groups[1].Value -eq 'tactician_yuno') {
                    $longestNeutralTopRun = 0
                    for ($neutralY = $minY; $neutralY -le [Math]::Min($minY + 1, $bitmap.Height - 1); $neutralY++) {
                        $currentNeutralTopRun = 0
                        for ($x = 0; $x -lt $frameWidth; $x++) {
                            $colour = $bitmap.GetPixel(($frame * $frameWidth) + $x, $neutralY)
                            $maximumChannel = [Math]::Max($colour.R, [Math]::Max($colour.G, $colour.B))
                            $minimumChannel = [Math]::Min($colour.R, [Math]::Min($colour.G, $colour.B))
                            $luminance = [int](($colour.R * 54 + $colour.G * 183 + $colour.B * 19) / 256)
                            if ($colour.A -gt 8 -and ($maximumChannel - $minimumChannel) -le 16 -and
                                $luminance -ge 70 -and $luminance -le 205) {
                                $currentNeutralTopRun++
                                if ($currentNeutralTopRun -gt $longestNeutralTopRun) {
                                    $longestNeutralTopRun = $currentNeutralTopRun
                                }
                            } else {
                                $currentNeutralTopRun = 0
                            }
                        }
                    }
                    if ($longestNeutralTopRun -gt 2) {
                        $failures += "$relative frame $frame suspiciousNeutralTopRun=${longestNeutralTopRun}px limit=2px"
                    }
                }
            }
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

foreach ($heroId in $heroIds) {
    if (-not $referenceHeights.ContainsKey($heroId) -or $referenceHeights[$heroId].Count -ne 3) {
        $failures += "$heroId is missing idle/attack/hurt reference-frame measurements"
        continue
    }
    $heights = @($referenceHeights[$heroId].Values)
    $minimumHeight = ($heights | Measure-Object -Minimum).Minimum
    $maximumHeight = ($heights | Measure-Object -Maximum).Maximum
    if ($minimumHeight -lt 208 -or $maximumHeight -gt 224 -or ($maximumHeight - $minimumHeight) -gt 8) {
        $failures += "$heroId motion reference heights are inconsistent: $($referenceHeights[$heroId] | ConvertTo-Json -Compress)"
    }
}

$comboPath = Join-Path $workspace 'src/Client/public/ui/battle-control-combo.png'
$combo = [System.Drawing.Bitmap]::new($comboPath)
try {
    if ($combo.Width -ne 220 -or $combo.Height -ne 104) {
        $failures += "src/Client/public/ui/battle-control-combo.png expected=220x104 actual=$($combo.Width)x$($combo.Height)"
    }
    $corners = @(
        $combo.GetPixel(0, 0).A,
        $combo.GetPixel($combo.Width - 1, 0).A,
        $combo.GetPixel(0, $combo.Height - 1).A,
        $combo.GetPixel($combo.Width - 1, $combo.Height - 1).A
    )
    if (($corners | Measure-Object -Maximum).Maximum -gt 8) {
        $failures += "src/Client/public/ui/battle-control-combo.png outer corners must be transparent: $($corners -join ',')"
    }
    $centreAlpha = $combo.GetPixel([int]($combo.Width / 2), [int]($combo.Height / 2)).A
    if ($centreAlpha -le 220) {
        $failures += "src/Client/public/ui/battle-control-combo.png centre alpha=$centreAlpha (expected >220)"
    }
}
finally { $combo.Dispose() }

if ($failures.Count -gt 0) {
    Write-Error ("Raster boundary audit failed:`n" + ($failures -join "`n"))
    exit 1
}

Write-Host "Raster boundary audit passed: $($files.Count) files; 8/4/4 hero frames, portraits, opaque combo centre, and minimum transparent gutter ${MinimumTransparentGutter}px."
