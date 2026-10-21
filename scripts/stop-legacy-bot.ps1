$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object {
  $_.CommandLine -match 'bot[/\\]index\.mjs' -and $_.CommandLine.Contains($taskRoot)
} | ForEach-Object {Stop-Process -Id $_.ProcessId -ErrorAction Stop;Write-Output 'Stopped this project''s previous Telegram polling process.'}
