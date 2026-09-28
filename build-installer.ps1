# Builds the WinNote installer: publishes the app (Native AOT) and packs it into an MSI.
#   powershell -ExecutionPolicy Bypass -File build-installer.ps1
# Output: artifacts\WinNote-<version>-x64.msi
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Native AOT's linker setup calls vswhere.exe, which the VS Installer doesn't put on PATH.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path $vsInstaller) { $env:PATH = "$vsInstaller;$env:PATH" }

$appDir = Join-Path $PSScriptRoot 'artifacts\app\win-x64'
if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }

Write-Host '== Publishing WinNote (Release, Native AOT, win-x64)'
dotnet publish src\WinNote\WinNote.csproj -c Release -r win-x64 -p:Platform=x64 -o $appDir
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Write-Host '== Building MSI'
dotnet build installer\WinNote.Installer.wixproj -c Release "-p:AppDir=$appDir" -o artifacts
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

Get-ChildItem artifacts\*.msi | ForEach-Object { Write-Host ("== Done: {0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) }
