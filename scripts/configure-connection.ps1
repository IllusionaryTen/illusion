$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Write-Output 'Set the public HTTPS address of your running Illusion relay once.'
Write-Output 'Future downloads will include that connection; users will not need to type it.'
Write-Output 'This does not create hosting or keep this laptop online while asleep.'
$taskRelay = (Read-Host 'Public HTTPS relay address').Trim()
if (-not $taskRelay.StartsWith('https://')) {throw 'Use your public HTTPS relay address. Local testing remains available inside Advanced settings.'}
New-Item -ItemType Directory -Path (Join-Path $taskRoot 'build') -Force | Out-Null
Push-Location $taskRoot
try {
  & node (Join-Path $PSScriptRoot 'connection-profile.mjs') --relay $taskRelay --out (Join-Path $taskRoot 'build\connection-setup.json')
  if ($LASTEXITCODE -ne 0) {throw 'The address was not saved. Check it and try again.'}
  $taskProfile = Get-Content -LiteralPath (Join-Path $taskRoot 'build\connection-setup.json') -Raw | ConvertFrom-Json
  $taskEnvPath = Join-Path $taskRoot '.env'
  $taskText = if (Test-Path -LiteralPath $taskEnvPath) {[System.IO.File]::ReadAllText($taskEnvPath)} else {[System.IO.File]::ReadAllText((Join-Path $taskRoot '.env.example'))}
  $taskLine = 'DEFAULT_RELAY_URL=' + $taskProfile.relay
  if ($taskText -match '(?m)^DEFAULT_RELAY_URL=') {$taskText = [regex]::Replace($taskText,'(?m)^DEFAULT_RELAY_URL=.*$', [System.Text.RegularExpressions.MatchEvaluator]{param($match) $taskLine})}
  else {$taskText += "`r`n$taskLine`r`n"}
  [System.IO.File]::WriteAllText($taskEnvPath,$taskText,[System.Text.UTF8Encoding]::new($false))
  & (Join-Path $PSScriptRoot 'build.ps1') -OutputName 'Illusion-0.6.1.exe'
  if ($LASTEXITCODE -ne 0) {throw 'The connection was saved but the build failed.'}
} finally {Pop-Location}
Write-Output 'Connection embedded. Restart the download bot to enable /connect and share the new build.'
Write-Output 'Address reachability is not verified by this setup. Keep the actual relay running.'
