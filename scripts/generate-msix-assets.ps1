$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$projectRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $projectRoot "assets\app-icon.png"
$outputDirectory = Join-Path $projectRoot "assets\msix"

if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
  throw "The source application icon was not found: $sourcePath"
}

[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$source = [System.Drawing.Image]::FromFile($sourcePath)

try {
  $assets = @{
    "icon.png" = @(50, 50)
    "Square44x44Logo.png" = @(44, 44)
    "Square44x44Logo.scale-200.png" = @(88, 88)
    "Square44x44Logo.targetsize-24_altform-unplated.png" = @(24, 24)
    "Square150x150Logo.png" = @(150, 150)
    "Square150x150Logo.scale-200.png" = @(300, 300)
    "Wide310x150Logo.scale-200.png" = @(620, 300)
    "SplashScreen.scale-200.png" = @(1240, 600)
    "LockScreenLogo.scale-200.png" = @(48, 48)
  }

  foreach ($asset in $assets.GetEnumerator()) {
    $width = $asset.Value[0]
    $height = $asset.Value[1]
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
      $graphics.Clear([System.Drawing.Color]::Transparent)
      $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
      $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
      $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
      $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
      $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

      $scale = [Math]::Min($width / $source.Width, $height / $source.Height)
      $drawWidth = [Math]::Max(1, [Math]::Round($source.Width * $scale))
      $drawHeight = [Math]::Max(1, [Math]::Round($source.Height * $scale))
      $x = [Math]::Floor(($width - $drawWidth) / 2)
      $y = [Math]::Floor(($height - $drawHeight) / 2)
      $graphics.DrawImage($source, $x, $y, $drawWidth, $drawHeight)

      $destination = Join-Path $outputDirectory $asset.Key
      $bitmap.Save($destination, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
      $graphics.Dispose()
      $bitmap.Dispose()
    }
  }
} finally {
  $source.Dispose()
}

Write-Host "Generated Microsoft Store assets in $outputDirectory"
