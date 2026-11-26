param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$project = Join-Path $root 'src/Ron1n.Mode/Ron1n.Mode.csproj'
$setupDir = Join-Path $dist 'installer-publish'
$portableDir = Join-Path $dist 'portable-publish'
New-Item -ItemType Directory -Force $dist | Out-Null
$shared = @($project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', "-p:Version=$Version", '-p:DebugType=none', '-p:Platform=x64')
dotnet publish @shared -o $setupDir
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed' }
dotnet publish @shared -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o $portableDir
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed' }
Copy-Item (Join-Path $portableDir 'ConsoleMode.exe') (Join-Path $dist 'ConsoleMode-Portable-x64.exe') -Force
$compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
if (-not (Test-Path $compiler)) { throw 'Inno Setup 6 is required' }
& $compiler "/DAppVersion=$Version" "/DSourceDir=$setupDir" "/DOutputDir=$dist" (Join-Path $PSScriptRoot 'ConsoleMode.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
Get-FileHash (Join-Path $dist '*-x64.exe') -Algorithm SHA256
