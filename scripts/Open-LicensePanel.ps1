$ErrorActionPreference = 'Stop'
$dataDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PEXBOT\LicenseHost\data'
$secretFile = Join-Path $dataDirectory 'admin-secret.xml'
if (-not (Test-Path -LiteralPath $secretFile)) { throw 'Painel do PEXBOT ainda não foi instalado.' }
$secure = Import-Clixml -LiteralPath $secretFile
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { Set-Clipboard ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
Start-Process 'http://127.0.0.1:5928/admin' | Out-Null
Write-Host 'Painel aberto. A senha foi copiada; cole-a no campo de acesso.'
