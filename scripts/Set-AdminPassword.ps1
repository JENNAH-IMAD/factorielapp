$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$secure = Read-Host 'Mot de passe administrateur (14 caracteres minimum)' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    if ($plain.Length -lt 14) { throw '14 caracteres minimum.' }
    dotnet build "$projectRoot/backend/Pointage.Api.csproj" --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Compilation impossible.' }
    $hash = $plain | dotnet "$projectRoot/backend/bin/Debug/net10.0/Pointage.Api.dll" --hash-password
    if ($LASTEXITCODE -ne 0) { throw 'Generation impossible.' }
    # Hash only, stored outside source control by the .NET user-secrets provider.
    dotnet user-secrets set 'Admin:PasswordHash' "$hash" --project "$projectRoot/backend"
    Write-Host 'Hash enregistre dans les secrets utilisateur .NET. Pour Docker, transferez ce hash dans ADMIN_PASSWORD_HASH sans publier le fichier .env.'
} finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $plain = $null; $hash = $null }
