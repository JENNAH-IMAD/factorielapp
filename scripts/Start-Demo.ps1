$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (-not (Test-Path 'frontend/node_modules')) { Push-Location frontend; try { npm ci; if ($LASTEXITCODE -ne 0) { throw 'npm ci a echoue.' } } finally { Pop-Location } }
    Push-Location frontend
    try { npm run build; if ($LASTEXITCODE -ne 0) { throw 'Build React en echec.' } } finally { Pop-Location }
    New-Item -ItemType Directory -Force backend/wwwroot | Out-Null
    Copy-Item frontend/dist/* backend/wwwroot -Recurse -Force
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Demo = 'true'
    Write-Host 'Demonstration : http://127.0.0.1:5080 (Ctrl+C pour arreter). Donnees fictives uniquement.'
    dotnet run --project backend --urls http://127.0.0.1:5080
    if ($LASTEXITCODE -ne 0) { throw 'Impossible de démarrer la démonstration.' }
} finally { Pop-Location }
