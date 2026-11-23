param([Alias('GamePath')][string]$VintageStoryPath='')
$ErrorActionPreference='Stop'
if (-not $VintageStoryPath) { $VintageStoryPath=Join-Path $env:APPDATA 'Vintagestory' }
& (Join-Path $PSScriptRoot 'BUILD-WINDOWS.ps1') -GamePath $VintageStoryPath -NoInstall
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
& dotnet run --project (Join-Path $PSScriptRoot 'tests\Regression.csproj') -c Release "-p:VintageStoryPath=$VintageStoryPath"
if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed' }
Write-Host 'Build and regressions complete. Install in a backup world and run RELEASE-PLAYTEST.md tests.'
