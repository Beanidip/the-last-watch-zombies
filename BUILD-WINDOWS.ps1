# Builds and installs the SINGLE merged The Last Watch Zombies mod.
# Usage: .\BUILD-WINDOWS.ps1 -GamePath "$env:APPDATA\Vintagestory"
# Existing merged/split zombie mod ZIPs are backed up OUTSIDE Mods on upgrade.
param([Alias('GamePath')][string]$VintageStoryPath='', [string]$ModsPath='', [switch]$NoInstall)
$ErrorActionPreference='Stop'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET 10 SDK and reopen PowerShell.' }
if (-not $VintageStoryPath) { $VintageStoryPath=Join-Path $env:APPDATA 'Vintagestory' }
if (-not (Test-Path (Join-Path $VintageStoryPath 'VintagestoryAPI.dll'))) { throw 'Missing VintagestoryAPI.dll: use -GamePath to select your installation folder.' }
$project=Join-Path $PSScriptRoot 'TheLastWatchZombies.csproj'
$modInfo=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'modinfo.json') -Raw | ConvertFrom-Json
$version=[string]$modInfo.version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Missing modinfo.json version.' }
$packageName="thelastwatchzombies_$version.zip"
& dotnet build $project -c Release -m:1 -t:Rebuild "-p:VintageStoryPath=$VintageStoryPath"
if ($LASTEXITCODE -ne 0) { throw 'The Last Watch Zombies build failed; old mods were not touched.' }
$output=Join-Path $PSScriptRoot ("dist\" + $packageName)
if (-not (Test-Path -LiteralPath $output)) { throw "Missing compiled output: $output" }
if (-not $NoInstall) {
    if (-not $ModsPath) { $ModsPath=Join-Path $env:APPDATA 'VintagestoryData\Mods' }
    New-Item -ItemType Directory -Force -Path $ModsPath | Out-Null
    $old=@(Get-ChildItem -LiteralPath $ModsPath -File -Filter '*.zip' | Where-Object {
        $_.Name -match '^(seventhnightzombies|thelastwatch|thelastwatchzombies)[_-].*\.zip$'
    })
    $moved=@()
    try {
        if ($old.Count -gt 0) {
            $backupDir=Join-Path (Split-Path -Parent $ModsPath) 'TheLastWatchModBackups'
            New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
            foreach ($entry in $old) {
                $backupName='{0}_{1}_{2}{3}' -f $entry.BaseName, (Get-Date -Format 'yyyyMMdd_HHmmss_fff'), ([guid]::NewGuid().ToString('N').Substring(0,6)), $entry.Extension
                $target=Join-Path $backupDir $backupName
                Move-Item -LiteralPath $entry.FullName -Destination $target -ErrorAction Stop
                $moved+=@{ From=$entry.FullName; To=$target }
                Write-Host "Backed up previous mod ZIP to $target"
            }
        }
        Copy-Item -LiteralPath $output -Destination (Join-Path $ModsPath $packageName) -Force -ErrorAction Stop
    } catch {
        foreach ($item in $moved) {
            if (Test-Path -LiteralPath $item.To) { Move-Item -LiteralPath $item.To -Destination $item.From -Force -ErrorAction SilentlyContinue }
        }
        throw
    }
    Write-Host "Installed stable The Last Watch Zombies $version into $ModsPath"
}
Write-Host "SUCCESS: $output"
