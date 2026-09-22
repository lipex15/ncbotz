param(
    [string]$ServerDirectory = (Join-Path $PSScriptRoot '../artifacts/license-server'),
    [int]$Port = 5927,
    [int]$AdminPort = 5928
)

$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path (Join-Path $ServerDirectory 'BotNC.LicenseServer.exe')).Path
$dataDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PEXBOT-LicenseServer'
$database = Join-Path $dataDirectory 'licenses.db'
$env:PEXBOT_LICENSE_DATA = $dataDirectory
$env:ASPNETCORE_URLS = "http://127.0.0.1:${Port};http://127.0.0.1:${AdminPort}"
$env:PEXBOT_ADMIN_PORT = $AdminPort.ToString()

if (-not (Test-Path -LiteralPath $database)) {
    $secret = Read-Host 'Crie uma senha forte para o painel (mínimo 16 caracteres)' -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    try {
        $env:PEXBOT_ADMIN_INITIAL_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    if ($env:PEXBOT_ADMIN_INITIAL_PASSWORD.Length -lt 16) {
        $env:PEXBOT_ADMIN_INITIAL_PASSWORD = $null
        throw 'A senha precisa ter pelo menos 16 caracteres.'
    }
}

Write-Host "Painel local: http://127.0.0.1:$AdminPort/admin"
Write-Host 'Deixe esta janela aberta enquanto quiser aceitar novos logins e receber alertas.'
try { & $executable }
finally { $env:PEXBOT_ADMIN_INITIAL_PASSWORD = $null }
