$ErrorActionPreference = 'Stop'
$dataDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PEXBOT\LicenseHost\data'
$secretFile = Join-Path $dataDirectory 'admin-secret.xml'
if (-not (Test-Path -LiteralPath $secretFile)) { throw 'Painel do PEXBOT ainda não foi instalado.' }
function Test-AdminHealth {
    try {
        $health = Invoke-WebRequest -Uri 'http://127.0.0.1:5928/health' -UseBasicParsing -TimeoutSec 2
        return $health.StatusCode -eq 200
    } catch { return $false }
}
if (-not (Test-AdminHealth)) {
    Start-ScheduledTask -TaskName 'PEXBOT License Host'
    $deadline = [DateTime]::UtcNow.AddSeconds(25)
    do {
        Start-Sleep -Milliseconds 500
        $ready = Test-AdminHealth
    } until ($ready -or [DateTime]::UtcNow -ge $deadline)
    if (-not $ready) { throw 'O servidor do painel não respondeu. Verifique a tarefa PEXBOT License Host e os logs antes de tentar novamente.' }
}
$secure = Import-Clixml -LiteralPath $secretFile
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { Set-Clipboard ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
Start-Process 'http://127.0.0.1:5928/admin' | Out-Null
Write-Host 'Painel aberto. A senha foi copiada; cole-a no campo de acesso.'
