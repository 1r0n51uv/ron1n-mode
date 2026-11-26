param([string]$Version = '1.7.1.6')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$publish = Join-Path $dist 'publish'
New-Item -ItemType Directory -Force $dist | Out-Null
dotnet publish (Join-Path $root 'src/Ron1n.Mode/Ron1n.Mode.csproj') -c Release -r win-x64 --self-contained true "-p:Version=$Version" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Copy-Item (Join-Path $publish 'ConsoleMode.exe') (Join-Path $dist 'ConsoleMode-Portable-x64.exe') -Force
$compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
if (-not (Test-Path $compiler)) { throw 'Inno Setup 6 is required' }
& $compiler "/DAppVersion=$Version" "/DSourceDir=$publish" "/DOutputDir=$dist" (Join-Path $PSScriptRoot 'ConsoleMode.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
Get-FileHash (Join-Path $dist '*-x64.exe') -Algorithm SHA256
