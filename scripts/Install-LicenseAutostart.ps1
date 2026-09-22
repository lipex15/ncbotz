$ErrorActionPreference = 'Stop'
$runner = (Resolve-Path (Join-Path $PSScriptRoot 'Run-LicenseHost.ps1')).Path
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument (
    '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}"' -f $runner
)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName 'PEXBOT License Host' -Action $action -Trigger $trigger `
    -Settings $settings -Description 'Login por máquina e alertas do PEXBOT.' -Force | Out-Null
Write-Host 'Inicialização automática do login PEXBOT configurada.'
