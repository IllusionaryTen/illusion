param([switch]$TestBuild,[string]$OutputName='Illusion.exe',[string]$RelayUrl='',[string]$NetworkName='')
$ErrorActionPreference = 'Stop'
$taskSha256 = { param([string]$path) $sha=[System.Security.Cryptography.SHA256]::Create();$stream=[System.IO.File]::OpenRead($path);try{[System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}finally{$stream.Dispose();$sha.Dispose()} }
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskPackage = Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'package.json') | ConvertFrom-Json
$taskVersion = [string]$taskPackage.version
if ($taskVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'package.json must use a three-part numeric version.' }
if ($OutputName -eq 'Illusion.exe') { $OutputName = "Illusion-$taskVersion.exe" }
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskFramework = Split-Path -Parent $taskCompiler
$taskWpf = Join-Path $taskFramework 'WPF'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Windows .NET Framework 4.8 compiler was not found.' }
& (Join-Path $PSScriptRoot 'make-icon.ps1')
New-Item -ItemType Directory -Path (Join-Path $taskRoot 'dist') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $taskRoot 'build') -Force | Out-Null
Push-Location $taskRoot
try {& node (Join-Path $PSScriptRoot 'build-translations.mjs') (Join-Path $taskRoot 'build\translations.json');if ($LASTEXITCODE -ne 0) {throw 'Translation resource build failed.'}} finally {Pop-Location}
if ($OutputName -notmatch '^[A-Za-z0-9_.-]+\.exe$') {throw 'OutputName must be an executable filename, without directories.'}
$taskConnectionPath = Join-Path $taskRoot 'build\connection.json'
$taskConnectionArgs = @('--env-file-if-exists=.env',(Join-Path $PSScriptRoot 'connection-profile.mjs'),'--out',$taskConnectionPath)
if ($RelayUrl) {$taskConnectionArgs += @('--relay',$RelayUrl)}
if ($NetworkName) {$taskConnectionArgs += @('--name',$NetworkName)}
Push-Location $taskRoot
try {& node @taskConnectionArgs;if ($LASTEXITCODE -ne 0) {throw 'Connection profile validation failed.'}} finally {Pop-Location}
& node --env-file-if-exists=.env (Join-Path $PSScriptRoot 'write-legal.mjs') (Join-Path $taskRoot 'build\legal.json')
if ($LASTEXITCODE -ne 0) {throw 'Policy resource build failed.'}
$taskReferences = @('System.dll','System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Security.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $taskFramework $_) }
$taskReferences += @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path $taskWpf $_) }
$taskExe = Join-Path $taskRoot ('dist\' + $OutputName)
$taskArguments = @('/nologo','/target:winexe','/optimize+','/platform:anycpu','/utf8output',('/out:' + $taskExe),('/resource:' + (Join-Path $taskRoot 'desktop\Main.xaml') + ',Main.xaml'))
if (Test-Path -LiteralPath (Join-Path $taskRoot 'desktop\Illusion.ico')) { $taskArguments += '/win32icon:' + (Join-Path $taskRoot 'desktop\Illusion.ico'); $taskArguments += '/resource:' + (Join-Path $taskRoot 'desktop\Illusion.ico') + ',Illusion.ico' }
$taskArguments += $taskReferences
$taskArguments += '/resource:' + $taskConnectionPath + ',connection.json'
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'desktop') -Filter '*.cs' | ForEach-Object {$_.FullName})
$taskVersionSource = Join-Path $taskRoot 'build\VersionInfo.cs'
$taskVersionCSharp = 'namespace Illusion { static class BuildInfo { internal const string Version = "' + $taskVersion + '"; } }'
[System.IO.File]::WriteAllText($taskVersionSource, $taskVersionCSharp, (New-Object System.Text.UTF8Encoding($false)))
$taskSources += $taskVersionSource
$taskArguments += '/resource:' + (Join-Path $taskRoot 'shared\fonts.json') + ',fonts.json'
$taskArguments += '/resource:' + (Join-Path $taskRoot 'build\translations.json') + ',translations.json'
$taskArguments += '/resource:' + (Join-Path $taskRoot 'build\legal.json') + ',legal.json'
$taskArguments += '/resource:' + (Join-Path $taskRoot 'web\sounds\illusionsfx.mp3') + ',illusionsfx.mp3'
$taskArguments += $taskSources
& $taskCompiler @taskArguments
if ($LASTEXITCODE -ne 0) { throw 'Desktop compilation failed.' }
$taskSize = (Get-Item -LiteralPath $taskExe).Length
if ($taskSize -gt 50000000) { throw 'Release exceeds the Telegram bot upload limit.' }
$taskHash = & $taskSha256 $taskExe
$taskManifest = [ordered]@{name='Illusion';version=$taskVersion;file=$OutputName;bytes=$taskSize;sha256=$taskHash;notes='1.4: device-local per-chat mute with grouped alerts, portable look import/export with a strict whitelist, and per-contact wallpapers stored only on the device. No wire changes.'}
$taskProfileRelease = Join-Path $taskRoot 'dist\Illusion-Connection.illusion'
Copy-Item -LiteralPath $taskConnectionPath -Destination $taskProfileRelease -Force
$taskProfile = Get-Content -LiteralPath $taskProfileRelease -Raw | ConvertFrom-Json
$taskManifest['connection'] = [ordered]@{file='Illusion-Connection.illusion';bytes=(Get-Item -LiteralPath $taskProfileRelease).Length;sha256=(& $taskSha256 $taskProfileRelease);configured=[bool]$taskProfile.relay}
$taskArchive = Join-Path $taskRoot 'dist\Illusion-Windows.zip'
Compress-Archive -LiteralPath @($taskExe,(Join-Path $taskRoot 'desktop\START-HERE.txt')) -DestinationPath $taskArchive -Force
$taskArchiveHash = & $taskSha256 $taskArchive
$taskManifest['archive'] = [ordered]@{file='Illusion-Windows.zip';bytes=(Get-Item -LiteralPath $taskArchive).Length;sha256=$taskArchiveHash}
$taskManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRoot 'dist\release.json') -Encoding utf8
[System.IO.File]::WriteAllText((Join-Path $taskRoot 'dist\checksums.txt'), "$taskHash  $OutputName`r`n$taskArchiveHash  Illusion-Windows.zip`r`n")
Write-Output "Built $taskExe ($taskSize bytes)"
Write-Output "SHA256: $taskHash"
if ($TestBuild) {
  $taskTestArgs = @('/nologo','/target:exe','/optimize+',('/out:' + (Join-Path $taskRoot 'dist\DesktopTests.exe')),('/main:Illusion.DesktopTests'),('/resource:' + (Join-Path $taskRoot 'desktop\Main.xaml') + ',Main.xaml'),('/resource:' + (Join-Path $taskRoot 'desktop\Illusion.ico') + ',Illusion.ico')) + $taskReferences
  $taskTestArgs += '/resource:' + $taskConnectionPath + ',connection.json'
  $taskTestArgs += '/resource:' + (Join-Path $taskRoot 'shared\fonts.json') + ',fonts.json'
$taskTestArgs += '/resource:' + (Join-Path $taskRoot 'build\translations.json') + ',translations.json'
$taskTestArgs += '/resource:' + (Join-Path $taskRoot 'build\legal.json') + ',legal.json'
$taskTestArgs += '/resource:' + (Join-Path $taskRoot 'web\sounds\illusionsfx.mp3') + ',illusionsfx.mp3'
$taskTestArgs += $taskSources
  $taskTestArgs += (Join-Path $taskRoot 'tests\DesktopTests.cs')
  & $taskCompiler @taskTestArgs
  if ($LASTEXITCODE -ne 0) { throw 'Desktop test compilation failed.' }
  $taskCloudArgs = $taskTestArgs | ForEach-Object { if ($_ -eq '/main:Illusion.DesktopTests') {'/main:Illusion.CloudDesktopTests'} elseif ($_ -eq ('/out:' + (Join-Path $taskRoot 'dist\DesktopTests.exe'))) {'/out:' + (Join-Path $taskRoot 'dist\CloudDesktopTests.exe')} elseif ($_ -eq (Join-Path $taskRoot 'tests\DesktopTests.cs')) {(Join-Path $taskRoot 'tests\CloudDesktopTests.cs')} else {$_} }
  & $taskCompiler @taskCloudArgs
  if ($LASTEXITCODE -ne 0) { throw 'Cloud desktop test compilation failed.' }
}
