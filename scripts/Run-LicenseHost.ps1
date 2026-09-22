param(
    [switch]$LaunchOnce
)

$ErrorActionPreference = 'Stop'
$hostRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PEXBOT\LicenseHost'
$dataDirectory = Join-Path $hostRoot 'data'
$serverExecutable = Join-Path $hostRoot 'server\BotNC.LicenseServer.exe'
$secretFile = Join-Path $dataDirectory 'admin-secret.xml'
$ngrokExecutable = Get-ChildItem (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Microsoft\WinGet\Packages') `
    -Filter ngrok.exe -Recurse -File | Select-Object -First 1 -ExpandProperty FullName

if (-not (Test-Path -LiteralPath $serverExecutable)) { throw 'Servidor de login não instalado.' }
if (-not (Test-Path -LiteralPath $secretFile)) { throw 'Segredo local do painel não encontrado.' }
if (-not $ngrokExecutable) { throw 'Conector HTTPS não instalado.' }

function Start-LicenseServer {
    $secure = Import-Clixml -LiteralPath $secretFile
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $env:PEXBOT_ADMIN_INITIAL_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    $env:PEXBOT_LICENSE_DATA = $dataDirectory
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:5927;http://127.0.0.1:5928'
    $env:PEXBOT_ADMIN_PORT = '5928'
    try { return Start-Process -FilePath $serverExecutable -WindowStyle Hidden -PassThru }
    finally { $env:PEXBOT_ADMIN_INITIAL_PASSWORD = $null }
}

function Wait-LicenseServer {
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try {
            $health = Invoke-RestMethod 'http://127.0.0.1:5927/health' -TimeoutSec 2
            if ($health.status -eq 'ok') { return }
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    throw 'O servidor de login não respondeu localmente.'
}

function Start-LicenseTunnel {
    $logFile = Join-Path $dataDirectory 'ngrok.log'
    return Start-Process -FilePath $ngrokExecutable -ArgumentList @(
        'http', '5927', '--log', $logFile, '--log-format', 'json'
    ) -WindowStyle Hidden -PassThru
}

try {
    $currentHealth = Invoke-RestMethod 'http://127.0.0.1:5927/health' -TimeoutSec 2
    if ($currentHealth.status -ne 'ok') { throw 'Resposta local inválida.' }
    $server = Get-Process 'BotNC.LicenseServer' -ErrorAction Stop | Select-Object -First 1
}
catch {
    $server = Start-LicenseServer
    Wait-LicenseServer
}

try {
    $currentTunnels = Invoke-RestMethod 'http://127.0.0.1:4040/api/tunnels' -TimeoutSec 2
    if (-not ($currentTunnels.tunnels | Where-Object proto -eq 'https')) { throw 'Túnel HTTPS ausente.' }
    $tunnel = Get-Process 'ngrok' -ErrorAction Stop | Select-Object -First 1
}
catch {
    $tunnel = Start-LicenseTunnel
}

if ($LaunchOnce) {
    Write-Output "server=$($server.Id) tunnel=$($tunnel.Id)"
    return
}

while ($true) {
    Start-Sleep -Seconds 5
    if ($server.HasExited) {
        $server = Start-LicenseServer
        Wait-LicenseServer
    }
    if ($tunnel.HasExited) { $tunnel = Start-LicenseTunnel }
}
