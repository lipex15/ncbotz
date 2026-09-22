$ErrorActionPreference = 'Stop'
$hostRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PEXBOT\LicenseHost'
New-Item -ItemType Directory -Force -Path $hostRoot | Out-Null
$runner = Join-Path $hostRoot 'Run-LicenseHost.ps1'
$panel = Join-Path $hostRoot 'Open-LicensePanel.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Run-LicenseHost.ps1') -Destination $runner -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Open-LicensePanel.ps1') -Destination $panel -Force
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument (
    '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}"' -f $runner
)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName 'PEXBOT License Host' -Action $action -Trigger $trigger `
    -Settings $settings -Description 'Login por máquina e alertas do PEXBOT.' -Force | Out-Null
$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop 'Painel PEXBOT.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = 'powershell.exe'
$shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $panel
$shortcut.WorkingDirectory = $hostRoot
$shortcut.IconLocation = Join-Path $hostRoot 'server\BotNC.LicenseServer.exe'
$shortcut.Save()
Write-Host 'Inicialização automática do login PEXBOT configurada.'
Write-Host "Atalho do painel criado em: $shortcutPath"
