$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskIconDirectory = Join-Path $taskRoot 'web\icons'
New-Item -ItemType Directory -Path $taskIconDirectory -Force | Out-Null
foreach ($taskSize in @(192,512)) {
  $taskBitmap = [System.Drawing.Bitmap]::new($taskSize,$taskSize)
  $taskGraphics = [System.Drawing.Graphics]::FromImage($taskBitmap)
  $taskGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $taskGraphics.Clear([System.Drawing.Color]::FromArgb(9,12,19))
  $taskGraphics.ScaleTransform($taskSize / 256.0,$taskSize / 256.0)
  $taskBlue = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(70,122,255),9)
  $taskWhite = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(240,247,255),7)
  $taskGraphics.TranslateTransform(128,128)
  $taskGraphics.RotateTransform(-27)
  $taskGraphics.DrawEllipse($taskBlue,-110,-61,220,122)
  $taskGraphics.DrawEllipse($taskBlue,-91,-43,182,86)
  $taskGraphics.ResetTransform()
  $taskGraphics.ScaleTransform($taskSize / 256.0,$taskSize / 256.0)
  $taskPoints = [System.Drawing.Point[]]@([System.Drawing.Point]::new(128,72),[System.Drawing.Point]::new(184,128),[System.Drawing.Point]::new(128,184),[System.Drawing.Point]::new(72,128))
  $taskGraphics.DrawPolygon($taskWhite,$taskPoints)
  $taskBitmap.Save((Join-Path $taskIconDirectory "icon-$taskSize.png"),[System.Drawing.Imaging.ImageFormat]::Png)
  $taskWhite.Dispose();$taskBlue.Dispose();$taskGraphics.Dispose();$taskBitmap.Dispose()
}
Write-Output 'Created Illusion Android home-screen icons.'
