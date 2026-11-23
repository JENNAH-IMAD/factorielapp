param([string]$Instance = '.\SQLEXPRESS')
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Database)) {
    $env:ConnectionStrings__Database = "Server=$Instance;Database=API_Sage_Factorial;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
}
