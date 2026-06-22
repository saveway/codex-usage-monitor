param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'app.ico')
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 256)
$images = foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $margin = [Math]::Max(1, [int]($size * 0.07))
    $diameter = $size - ($margin * 2) - 1
    $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 19, 58, 57))
    $ring = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 76, 208, 151), [Math]::Max(1.4, $size * 0.075))
    $mark = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(1.2, $size * 0.055))
    $needle = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 251, 191, 36), [Math]::Max(1.4, $size * 0.065))
    $needle.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $needle.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $graphics.FillEllipse($background, $margin, $margin, $diameter, $diameter)
    $graphics.DrawEllipse($ring, $margin, $margin, $diameter, $diameter)
    $center = $size / 2.0
    foreach ($angle in @(205, 245, 295, 335)) {
        $r1 = $size * 0.29
        $r2 = $size * 0.36
        $radians = $angle * [Math]::PI / 180
        $graphics.DrawLine(
            $mark,
            [single]($center + [Math]::Cos($radians) * $r1),
            [single]($center + [Math]::Sin($radians) * $r1),
            [single]($center + [Math]::Cos($radians) * $r2),
            [single]($center + [Math]::Sin($radians) * $r2))
    }
    $needleAngle = 315 * [Math]::PI / 180
    $graphics.DrawLine(
        $needle,
        [single]$center,
        [single]$center,
        [single]($center + [Math]::Cos($needleAngle) * $size * 0.25),
        [single]($center + [Math]::Sin($needleAngle) * $size * 0.25))
    $hub = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $hubSize = [Math]::Max(2, $size * 0.13)
    $graphics.FillEllipse($hub, $center - $hubSize / 2, $center - $hubSize / 2, $hubSize, $hubSize)

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    $hub.Dispose()
    $needle.Dispose()
    $mark.Dispose()
    $ring.Dispose()
    $background.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()

    [pscustomobject]@{ Size = $size; Bytes = $bytes }
}

$file = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
$writer = New-Object System.IO.BinaryWriter($file)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$images.Count)
$offset = 6 + (16 * $images.Count)
foreach ($image in $images) {
    $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$image.Bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) {
    $writer.Write($image.Bytes)
}
$writer.Dispose()
$file.Dispose()

Write-Host "Generated $OutputPath with $($images.Count) sizes."
