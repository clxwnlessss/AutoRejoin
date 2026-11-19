# Собирает AutoRejoin и упаковывает AutoRejoin.zip для релиза на GitHub.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
dotnet build (Join-Path $root 'AutoRejoin.csproj') -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Сборка не прошла' }

$out = Join-Path $root 'bin\Release\net48'
$stage = Join-Path $root 'bin\release-stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null
Copy-Item (Join-Path $out 'AutoRejoin.exe'), (Join-Path $out 'Microsoft.Web.WebView2.Core.dll'), (Join-Path $out 'Microsoft.Web.WebView2.Wpf.dll') $stage
Copy-Item (Join-Path $root 'engine.ps1'), (Join-Path $root 'lib\WebView2Loader.dll') $stage
Copy-Item (Join-Path $root 'Как пользоваться.txt') $stage -ErrorAction SilentlyContinue

$zip = Join-Path $root 'AutoRejoin.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
$version = (Get-Item (Join-Path $out 'AutoRejoin.exe')).VersionInfo.FileVersion
Write-Host "Готово: $zip (версия $version)"
