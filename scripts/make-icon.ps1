$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskImages = @()
foreach ($taskSize in @(16,32,48,256)) {
  $taskBitmap = [System.Drawing.Bitmap]::new($taskSize,$taskSize)
  $taskGraphics = [System.Drawing.Graphics]::FromImage($taskBitmap)
  $taskGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $taskGraphics.ScaleTransform($taskSize / 256.0,$taskSize / 256.0)
  $taskGraphics.Clear([System.Drawing.Color]::FromArgb(9,12,19))
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
  $taskStream = [System.IO.MemoryStream]::new()
  $taskBitmap.Save($taskStream,[System.Drawing.Imaging.ImageFormat]::Png)
  $taskImages += ,@{Size=$taskSize;Bytes=$taskStream.ToArray()}
  $taskStream.Dispose();$taskWhite.Dispose();$taskBlue.Dispose();$taskGraphics.Dispose();$taskBitmap.Dispose()
}
$taskOutput = [System.IO.File]::Create((Join-Path $taskRoot 'desktop\Illusion.ico'))
$taskWriter = [System.IO.BinaryWriter]::new($taskOutput)
try {
  $taskWriter.Write([uint16]0);$taskWriter.Write([uint16]1);$taskWriter.Write([uint16]$taskImages.Count)
  $taskOffset = 6 + 16 * $taskImages.Count
  foreach ($taskImage in $taskImages) {
    $taskDimension = if ($taskImage.Size -eq 256) {0} else {$taskImage.Size}
    $taskWriter.Write([byte]$taskDimension);$taskWriter.Write([byte]$taskDimension);$taskWriter.Write([byte]0);$taskWriter.Write([byte]0)
    $taskWriter.Write([uint16]1);$taskWriter.Write([uint16]32);$taskWriter.Write([uint32]$taskImage.Bytes.Length);$taskWriter.Write([uint32]$taskOffset)
    $taskOffset += $taskImage.Bytes.Length
  }
  foreach ($taskImage in $taskImages) {$taskWriter.Write([byte[]]$taskImage.Bytes)}
} finally {$taskWriter.Dispose();$taskOutput.Dispose()}
Write-Output 'Created original Illusion orbital icon.'
