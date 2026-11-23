param([switch]$ImportOnly, [switch]$ConnectFactorial, [string]$Instance = '.\SQLEXPRESS')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$keySupplied = $false
Push-Location $root
try {
    & "$PSScriptRoot/Initialize-LocalDatabase.ps1" -Instance $Instance
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Demo = 'false'
    if ($ImportOnly) {
        $secure = Read-Host 'Cle API Factorial Production (saisie masquee)' -AsSecureString
        $env:ImportFactorialProduction = 'true'
        $key = [Net.NetworkCredential]::new('', $secure).Password
        $key | dotnet run --project backend --no-launch-profile
        if ($LASTEXITCODE -ne 0) { throw 'Import Factorial en echec.' }
        return
    }
    if (-not ((dotnet user-secrets list --project backend) -match '^Admin:PasswordHash = ')) {
        & "$PSScriptRoot/Set-AdminPassword.ps1"
    }
    Push-Location frontend
    try {
        if (!(Test-Path node_modules)) { npm ci; if ($LASTEXITCODE -ne 0) { throw 'Installation React impossible.' } }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Compilation React impossible.' }
    } finally { Pop-Location }
    New-Item -ItemType Directory -Force backend/wwwroot | Out-Null
    Copy-Item frontend/dist/* backend/wwwroot -Recurse -Force
    if ($ConnectFactorial -and [string]::IsNullOrWhiteSpace($env:Factorial__ApiKey)) {
        $secure = Read-Host 'Nouvelle cle API Factorial (saisie masquee pour cette session)' -AsSecureString
        $env:Factorial__ApiKey = [Net.NetworkCredential]::new('', $secure).Password
        $keySupplied = $true
    }
    Write-Host 'Journal SQL Server : http://127.0.0.1:5081. Identifiant par defaut : admin.'
    dotnet run --project backend --no-launch-profile --urls http://127.0.0.1:5081
    if ($LASTEXITCODE -ne 0) { throw 'Impossible de démarrer le journal SQL Server.' }
} finally {
    $key = $null
    if ($keySupplied) { Remove-Item Env:Factorial__ApiKey -ErrorAction SilentlyContinue }
    Remove-Item Env:ConnectionStrings__Database,Env:ImportFactorialProduction -ErrorAction SilentlyContinue
    Pop-Location
}
