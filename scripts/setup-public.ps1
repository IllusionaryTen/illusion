$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskTools=Join-Path $taskRoot 'tools'
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
$taskBinary=Join-Path $taskTools 'cloudflared.exe'
$taskExpected='4d2490e74082c5d48dbfedaf92576fa5bd1702379ab88eb7faa0cfbe8dc891b6'
if (-not (Test-Path -LiteralPath $taskBinary) -or (Get-FileHash -LiteralPath $taskBinary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskExpected) {
  [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
  Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/cloudflare/cloudflared/releases/download/2026.10.1/cloudflared-windows-amd64.exe' -OutFile $taskBinary
  if ((Get-FileHash -LiteralPath $taskBinary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskExpected) {throw 'Official Cloudflare checksum did not match. The downloaded file will not be run.'}
}
Write-Output 'Verified Cloudflare tunnel client. Double-click Start-Public.cmd to start your public connection.'
