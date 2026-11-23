param([string]$Instance = '.\SQLEXPRESS')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$previousDatabase = $env:ConnectionStrings__Database
$previousFlag = $env:SaveFactorialProductionKey
$previousDemo = $env:Demo
try {
    & "$PSScriptRoot/Initialize-LocalDatabase.ps1" -Instance $Instance
    $env:SaveFactorialProductionKey = 'true'
    $env:Demo = 'false'
    $secure = Read-Host 'Cle API Factorial de production' -AsSecureString
    $key = [Net.NetworkCredential]::new('', $secure).Password
    if ([string]::IsNullOrWhiteSpace($key)) { throw 'Saisir une cle API.' }
    $key | dotnet run --project "$root/backend" --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw 'La cle n a pas ete enregistree.' }
} finally {
    $key = $null
    $env:ConnectionStrings__Database = $previousDatabase
    $env:SaveFactorialProductionKey = $previousFlag
    $env:Demo = $previousDemo
}
