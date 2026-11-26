param([string]$Setup = 'dist/ConsoleMode-Setup-x64.exe')

$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:RUNNER_TEMP 'ron1n-installed'
$installerLog = Join-Path $env:RUNNER_TEMP 'ron1n-install.log'
$appLog = Join-Path $env:LOCALAPPDATA 'ConsoleMode/startup.log'
$installedExe = Join-Path $installDir 'ConsoleMode.exe'
$started = Get-Date

function Get-InstalledProcess {
    Get-Process -Name ConsoleMode -ErrorAction SilentlyContinue |
        Where-Object Path -eq $installedExe
}

try {
    $installer = Start-Process -FilePath (Resolve-Path $Setup).Path -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=""',
        "/DIR=`"$installDir`"", "/LOG=`"$installerLog`""
    ) -PassThru
    if (!$installer.WaitForExit(120000)) { throw 'Installer timed out' }
    if ($installer.ExitCode -ne 0) { throw "Setup returned $($installer.ExitCode)" }
    if (!(Test-Path $installedExe) -or !(Test-Path (Join-Path $installDir 'ConsoleMode.installed'))) {
        throw 'Setup did not install Console Mode and its marker'
    }

    $deadline = (Get-Date).AddSeconds(35)
    while ((Get-Date) -lt $deadline) {
        $app = Get-InstalledProcess | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if ($app) {
            Start-Sleep -Seconds 3
            $app.Refresh()
            if (!$app.HasExited -and $app.MainWindowHandle -ne 0) {
                Write-Host "Installed setup launched a visible window: $($app.MainWindowTitle)"
                return
            }
        }
        Start-Sleep -Seconds 1
    }
    throw 'Setup finished but Console Mode did not open a visible window'
}
catch {
    if (Test-Path $appLog) { Get-Content $appLog -Tail 60 }
    if (Test-Path $installerLog) { Get-Content $installerLog -Tail 45 }
    Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$started; Id=1000,1001,1026} -ErrorAction SilentlyContinue |
        Where-Object Message -Match 'ConsoleMode\.exe' |
        Select-Object -First 5 | Format-List TimeCreated, Id, Message | Out-String | Write-Host
    throw
}
finally {
    Get-InstalledProcess | Stop-Process -ErrorAction SilentlyContinue
}
