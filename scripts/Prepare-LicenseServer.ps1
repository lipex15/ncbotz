$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $projectRoot 'src/BotNC.LicenseServer/BotNC.LicenseServer.csproj'
$output = Join-Path $projectRoot 'artifacts/license-server'
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=None -o $output
if ($LASTEXITCODE -ne 0) { throw 'Falha ao preparar o servidor de login.' }
Write-Host "Servidor preparado em: $output"
Write-Host 'Guarde essa pasta fora do repositório público. Ela não contém senhas.'
