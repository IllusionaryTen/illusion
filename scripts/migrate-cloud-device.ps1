param([switch]$CheckOnly)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskState=Get-Content -LiteralPath (Join-Path $taskRoot 'build/cloudflare-state.json') -Raw | ConvertFrom-Json
if (-not $taskState.migrated) {throw 'The account database must be migrated before changing this device connection.'}
$taskProfile=Join-Path $env:LOCALAPPDATA 'Illusion'
$taskSettingsPath=Join-Path $taskProfile 'settings.json'
if (-not (Test-Path -LiteralPath $taskSettingsPath)) {Write-Output 'No existing desktop profile needs a connection migration.';exit 0}
$taskSettings=Get-Content -LiteralPath $taskSettingsPath -Raw | ConvertFrom-Json
$taskOldOrigin=([string]$taskSettings.server).TrimEnd('/')
$taskNewOrigin=[string]$taskState.origin
if ($taskOldOrigin -eq $taskNewOrigin) {Write-Output 'Desktop already uses the hosted connection.';exit 0}
if ($taskNewOrigin -notmatch '^https://[a-z0-9.-]+\.workers\.dev$') {throw 'Unexpected hosted connection origin.'}
if (@(Get-Process | Where-Object {$_.ProcessName -like 'Illusion*'}).Count -gt 0) {throw 'Close Illusion before rerunning the deployment so its saved connection can be safely updated.'}
$taskSnapshot=Get-Content -LiteralPath (Join-Path $taskRoot 'build/cloud-migration.json') -Raw | ConvertFrom-Json
$taskAccount=@($taskSnapshot.tables.users | Where-Object {$_.username -eq ([string]$taskSettings.username).ToLowerInvariant()})
if ($taskAccount.Count -ne 1) {throw 'The saved desktop account does not match the migrated database; the original device files are unchanged.'}
[void][Reflection.Assembly]::LoadFile((Join-Path $taskRoot 'dist/Illusion-0.6.1.exe'))
$taskKeyHash=[Illusion.Crypto]::Fingerprint($taskOldOrigin+'/'+$taskAccount[0].username)
$taskOldKey=Join-Path $taskProfile ('key-'+$taskKeyHash+'.dat')
if (-not (Test-Path -LiteralPath $taskOldKey)) {throw 'The original device key was not found. Keep the original files and restore your key backup before changing networks.'}
$taskPrivate=[Illusion.Crypto]::Unprotect([IO.File]::ReadAllText($taskOldKey))
try {if ([Illusion.Crypto]::Public($taskPrivate) -cne $taskAccount[0].publicKey) {throw 'Device encryption identity does not match the migrated account.'}} finally {$taskPrivate=$null}
$taskCopies=@(@{Old=$taskOldKey;New=(Join-Path $taskProfile ('key-'+[Illusion.Crypto]::Fingerprint($taskNewOrigin+'/'+$taskAccount[0].username)+'.dat'))})
foreach ($taskKind in @(@{Prefix='pin-';Suffix='.txt'},@{Prefix='workspace-';Suffix='.dat'})) {
  $taskCopies+=@{Old=(Join-Path $taskProfile ($taskKind.Prefix+[Illusion.Crypto]::Fingerprint($taskOldOrigin+'/'+$taskAccount[0].id)+$taskKind.Suffix));New=(Join-Path $taskProfile ($taskKind.Prefix+[Illusion.Crypto]::Fingerprint($taskNewOrigin+'/'+$taskAccount[0].id)+$taskKind.Suffix))}
}
foreach ($taskCopy in $taskCopies) {if ((Test-Path -LiteralPath $taskCopy.Old) -and (Test-Path -LiteralPath $taskCopy.New) -and (Get-FileHash -LiteralPath $taskCopy.Old).Hash -ne (Get-FileHash -LiteralPath $taskCopy.New).Hash) {throw 'A different device file already exists for the hosted connection. Originals have not been changed.'}}
if ($CheckOnly) {Write-Output 'Verified the existing device key and planned local copies. No files were changed.';exit 0}
$taskBackup=Join-Path $taskRoot ('build/device-settings-before-cloud-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.json')
Copy-Item -LiteralPath $taskSettingsPath -Destination $taskBackup
foreach ($taskCopy in $taskCopies) {if (Test-Path -LiteralPath $taskCopy.Old) {Copy-Item -LiteralPath $taskCopy.Old -Destination $taskCopy.New -Force}}
foreach ($taskProperty in @('token','account')) {$taskSettings.PSObject.Properties.Remove($taskProperty)}
foreach ($taskPair in @(@{Name='server';Value=$taskNewOrigin},@{Name='networkName';Value='Illusion'},@{Name='lan';Value='false'})) { $taskSettings | Add-Member -NotePropertyName $taskPair.Name -NotePropertyValue $taskPair.Value -Force }
$taskJson=$taskSettings | ConvertTo-Json -Depth 32 -Compress
[IO.File]::WriteAllText($taskSettingsPath+'.tmp',$taskJson,(New-Object Text.UTF8Encoding($false)))
[IO.File]::Replace($taskSettingsPath+'.tmp',$taskSettingsPath,$taskBackup)
Write-Output 'Desktop connection updated. Verified DPAPI-protected key/history copies stay on this laptop; original files and settings backup are preserved. Sign in again with your existing password.'
