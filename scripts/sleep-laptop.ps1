param([ValidateRange(0,60)][int]$DelaySeconds=15,[switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
if ($CheckOnly) {Write-Output 'Windows sleep API is available. No sleep was requested.';exit 0}
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskStatus = Join-Path $taskRoot 'build\sleep-request.json'
New-Item -ItemType Directory -Path (Join-Path $taskRoot 'build') -Force | Out-Null
@{state='scheduled';delaySeconds=$DelaySeconds;requestedSleep='Suspend';force=$false} | ConvertTo-Json | Set-Content -LiteralPath $taskStatus -Encoding utf8
if ($DelaySeconds -gt 0) {Start-Sleep -Seconds $DelaySeconds}
try {
  @{state='requesting';requestedSleep='Suspend';force=$false} | ConvertTo-Json | Set-Content -LiteralPath $taskStatus -Encoding utf8
  $taskAccepted = [System.Windows.Forms.Application]::SetSuspendState([System.Windows.Forms.PowerState]::Suspend,$false,$false)
  @{state=$(if($taskAccepted){'accepted'}else{'failed'});requestedSleep='Suspend';force=$false} | ConvertTo-Json | Set-Content -LiteralPath $taskStatus -Encoding utf8
  if (-not $taskAccepted) {exit 1}
} catch {
  @{state='failed';reason=$_.Exception.Message} | ConvertTo-Json | Set-Content -LiteralPath $taskStatus -Encoding utf8
  exit 1
}
