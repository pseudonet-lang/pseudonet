# Génère les icônes du VSIX et des modèles (System.Drawing, lissé). Relancer après toute retouche de design.
Add-Type -AssemblyName System.Drawing
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$res  = Join-Path $root 'PseudoNet.Vsix\Resources'
New-Item -ItemType Directory $res -Force | Out-Null

function New-Canvas([int]$size, [string]$topHex, [string]$bottomHex) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.Clear([System.Drawing.Color]::Transparent)
    $r = [int]($size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, 2*$r, 2*$r, 180, 90); $path.AddArc($size-2*$r-1, 0, 2*$r, 2*$r, 270, 90)
    $path.AddArc($size-2*$r-1, $size-2*$r-1, 2*$r, 2*$r, 0, 90); $path.AddArc(0, $size-2*$r-1, 2*$r, 2*$r, 90, 90); $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0,0)), ([System.Drawing.Point]::new(0,$size)), ([System.Drawing.ColorTranslator]::FromHtml($topHex)), ([System.Drawing.ColorTranslator]::FromHtml($bottomHex))
    $g.FillPath($brush, $path)
    return @{ Bmp = $bmp; G = $g }
}
function Save-Png($c, [string]$path) { $c.G.Dispose(); $c.Bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $c.Bmp.Dispose() }
function Draw-Text($g, [string]$text, [string]$font, [single]$px, [string]$style, [System.Drawing.RectangleF]$rect, [string]$hex = '#FFFFFF') {
    $f = New-Object System.Drawing.Font $font, $px, ([System.Drawing.FontStyle]$style), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString($text, $f, (New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex))), $rect, $sf)
}

# Extension : lignes de pseudocode (blanc) + flèche orange vers le code
foreach ($s in 128, 200) {
    $c = New-Canvas $s '#2F6DB5' '#1B3F73'
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $orange = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#F28C28'))
    $h = $s * 0.085; $r = $h / 2
    foreach ($row in @(@(0.20, 0.30, 0.56), @(0.20, 0.45, 0.40), @(0.20, 0.60, 0.50))) {
        $x = $s * $row[0]; $y = $s * $row[1]; $w = $s * $row[2]
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $p.AddArc($x, $y, $h, $h, 90, 180); $p.AddArc($x + $w - $h, $y, $h, $h, 270, 180); $p.CloseFigure()
        $c.G.FillPath($white, $p)
    }
    $pts = @([System.Drawing.PointF]::new($s*0.62, $s*0.70), [System.Drawing.PointF]::new($s*0.84, $s*0.80), [System.Drawing.PointF]::new($s*0.62, $s*0.90))
    $c.G.FillPolygon($orange, $pts)
    Save-Png $c (Join-Path $res $(if ($s -eq 128) { 'icon.png' } else { 'preview.png' }))
}

# PseudoCode (console) : invite ">_" sur bleu
$c = New-Canvas 64 '#2F6DB5' '#1B3F73'
Draw-Text $c.G '>_' 'Consolas' 34 'Bold' ([System.Drawing.RectangleF]::new(0, 0, 64, 62))
Save-Png $c (Join-Path $root 'PseudoNet.Templates\PseudoCode\__TemplateIcon.png')

# PseudrawCode (fenêtre) : petit snake en segments carrés sur orange, pomme carrée alignée sur la grille
$c = New-Canvas 64 '#F39A3D' '#C4601A'
$body  = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#12683A'))
$head  = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#3FD27F'))
$apple = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#E02B2B'))
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$cell = 11; $ox = 4; $oy = 15            # grille 5 colonnes x 3 lignes, centrée
$snake = @(@(0,0), @(1,0), @(2,0), @(3,0), @(4,0), @(4,1), @(4,2), @(3,2), @(2,2), @(1,2))   # la dernière case est la tête
for ($i = 0; $i -lt $snake.Count; $i++) {
    $b = $body; if ($i -eq $snake.Count - 1) { $b = $head }
    $c.G.FillRectangle($b, $ox + $snake[$i][0] * $cell, $oy + $snake[$i][1] * $cell, $cell - 1, $cell - 1)
}
$hx = $ox + 1 * $cell; $hy = $oy + 2 * $cell
$c.G.FillRectangle($white, $hx + 1, $hy + 2, 3, 3)                       # œil
$c.G.FillRectangle($apple, $ox + 0 * $cell, $oy + 2 * $cell, $cell - 1, $cell - 1)   # pomme devant la tête
Save-Png $c (Join-Path $root 'PseudoNet.Templates\PseudrawCode\__TemplateIcon.png')

Get-ChildItem $res, (Join-Path $root 'PseudoNet.Templates\*\__TemplateIcon.png') | Select-Object Name, Length

# ── Icônes des exemples (PseudoNet.Samples/<id>/icon.png, 64x64) ─────────────
$samplesDir = Join-Path $root 'PseudoNet.Samples'
function Hex($h) { New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($h)) }

# Snake : même dessin que le modèle PseudrawCode
Copy-Item (Join-Path $root 'PseudoNet.Templates/PseudrawCode/__TemplateIcon.png') (Join-Path $samplesDir 'snake/icon.png') -Force

# Tetris : tétrominos colorés sur fond sombre
$c = New-Canvas 64 '#3A3F5C' '#1E2236'
$cell = 11
foreach ($b in @(@('#00C8E0',0,0,1,0,2,0,3,0), @('#9B59B6',0,2,1,2,2,2,1,1), @('#F2C230',4,1,5,1,4,2,5,2), @('#E74C3C',3,3,4,3,4,2,5,2))) { }
$blocks = @(
  @('#00C8E0', @(@(0,0),@(1,0),@(2,0),@(3,0))),
  @('#9B59B6', @(@(0,2),@(1,2),@(2,2),@(1,1))),
  @('#F2C230', @(@(3,2),@(4,2),@(3,1),@(4,1))),
  @('#2ECC71', @(@(1,3),@(2,3),@(2,2.0),@(3,3)))
)
$ox = 6; $oy = 14
foreach ($bl in $blocks) { foreach ($p in $bl[1]) { $c.G.FillRectangle((Hex $bl[0]), $ox + $p[0]*$cell, $oy + $p[1]*$cell, $cell-1, $cell-1) } }
Save-Png $c (Join-Path $samplesDir 'tetris/icon.png')

# Bounce : balle et sa trace
$c = New-Canvas 64 '#F39A3D' '#C4601A'
foreach ($t in @(@(14,44,6,'#FFD9B0'), @(24,34,8,'#FFE7CC'), @(34,26,10,'#FFF3E6'))) { $c.G.FillEllipse((Hex $t[3]), $t[0]-$t[2], $t[1]-$t[2], 2*$t[2], 2*$t[2]) }
$c.G.FillEllipse((Hex '#FFFFFF'), 46-13, 18-13, 26, 26)
$c.G.FillRectangle((Hex '#FFE0C2'), 6, 54, 52, 3)
Save-Png $c (Join-Path $samplesDir 'bounce/icon.png')

# Primes : "2 3 / 5 7"
$c = New-Canvas 64 '#2F6DB5' '#1B3F73'
Draw-Text $c.G '2 3' 'Consolas' 22 'Bold' ([System.Drawing.RectangleF]::new(0, 6, 64, 28))
Draw-Text $c.G '5 7' 'Consolas' 22 'Bold' ([System.Drawing.RectangleF]::new(0, 30, 64, 28))
Save-Png $c (Join-Path $samplesDir 'primes/icon.png')

# Bubble sort : barres croissantes, une en cours d'échange
$c = New-Canvas 64 '#27AE88' '#12705A'
$hs = @(14, 22, 30, 38, 46); $x = 7
for ($i = 0; $i -lt 5; $i++) {
    $brush = Hex '#FFFFFF'; if ($i -eq 3) { $brush = Hex '#FFB347' }
    $c.G.FillRectangle($brush, $x, 54 - $hs[$i], 8, $hs[$i]); $x += 10
}
Save-Png $c (Join-Path $samplesDir 'bubble-sort/icon.png')

# Guess the number : point d'interrogation
$c = New-Canvas 64 '#8E5BD1' '#5A2FA0'
Draw-Text $c.G '?' 'Segoe UI' 46 'Bold' ([System.Drawing.RectangleF]::new(0, -2, 64, 64))
Save-Png $c (Join-Path $samplesDir 'guess-the-number/icon.png')

Get-ChildItem $samplesDir -Recurse -Filter icon.png | Select-Object @{n='Sample';e={$_.Directory.Name}}, Length
