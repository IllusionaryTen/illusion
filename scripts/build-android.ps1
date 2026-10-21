param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location $taskRoot
$package = Get-Content -Raw -LiteralPath 'package.json' | ConvertFrom-Json
$version = [string]$package.version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'package.json must use a three-part numeric version.' }
$statePath = Join-Path $taskRoot 'build\cloudflare-state.json'
if (-not (Test-Path -LiteralPath $statePath)) { throw 'Run the public Illusion setup first so the APK can connect to its HTTPS service.' }
$state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
$origin = [string]$state.origin
if ($origin -notmatch '^https://[a-z0-9.-]+$') { throw 'The configured Illusion origin is not a valid HTTPS origin.' }
$sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
$androidUserHome = Join-Path $taskRoot 'build\android-user-home'
New-Item -ItemType Directory -Force -Path $androidUserHome | Out-Null
$env:ANDROID_USER_HOME = $androidUserHome
$env:JAVA_TOOL_OPTIONS = '-Duser.home=' + ($androidUserHome -replace '\\','/')
$platform = Get-ChildItem -LiteralPath (Join-Path $sdk 'platforms') -Directory -Filter 'android-37*' -ErrorAction SilentlyContinue | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'android.jar') } | Select-Object -First 1
if (-not $platform) { throw 'Android SDK Platform 37 is missing. Install it from Android Studio SDK Manager.' }
Set-Content -LiteralPath (Join-Path $taskRoot 'android\local.properties') -Value ('sdk.dir=' + ($sdk -replace '\\','/')) -Encoding ascii
$versionCode = [int](($version.Split('.') | ForEach-Object { $_.PadLeft(2,'0') }) -join '')
if (-not $NoBuild) {
  $env:GRADLE_USER_HOME = Join-Path $taskRoot 'build\android-gradle-user-home'
  & (Join-Path $taskRoot 'android\gradlew.bat') -p android assembleDebug --no-daemon --console=plain "-PillusionOrigin=$origin" "-PillusionVersion=$version" "-PillusionVersionCode=$versionCode"
  if ($LASTEXITCODE -ne 0) { throw "Android build failed with exit code $LASTEXITCODE." }
}
$built = Join-Path $taskRoot 'android\app\build\outputs\apk\debug\app-debug.apk'
if (-not (Test-Path -LiteralPath $built)) { throw 'Android APK build output was not found.' }
$dist = Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$file = "Illusion-$version.apk"
$target = Join-Path $dist $file
Copy-Item -LiteralPath $built -Destination $target -Force
$info = Get-Item -LiteralPath $target
if ($info.Length -gt 50000000) { Remove-Item -LiteralPath $target; throw 'The APK is larger than Telegram’s 50 MB upload limit.' }
$sha = [System.Security.Cryptography.SHA256]::Create()
$stream = [System.IO.File]::OpenRead($target)
try { $hash = [System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() } finally { $stream.Dispose(); $sha.Dispose() }
$release = [ordered]@{version=$version;file=$file;bytes=$info.Length;sha256=$hash;notes='Android preview build. Signed with the local Android debug key.'}
$release | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $dist 'android-release.json') -Encoding utf8
Write-Output "Built $target ($($info.Length) bytes, SHA-256 $hash)."
