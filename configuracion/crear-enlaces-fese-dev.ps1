# Crea 32 enlaces exclusivamente en DDCP_DEV, con acceso al modulo FESE.
# Repetible: conserva las cuentas y contrasenas ya existentes.
$ErrorActionPreference = 'Stop'
$proyecto = Join-Path $PSScriptRoot '..\IngestaDeDatos.Api\IngestaDeDatos.Api.csproj'
$ambienteAnterior = $env:ASPNETCORE_ENVIRONMENT
$ambienteDotnetAnterior = $env:DOTNET_ENVIRONMENT
$passwordAnterior = $env:FESE_ENLACES_PASSWORD
$clave = Read-Host 'Contrasena para las cuentas nuevas de desarrollo (no cambia las existentes)' -AsSecureString
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:FESE_ENLACES_PASSWORD = [System.Net.NetworkCredential]::new('', $clave).Password
    dotnet run --project $proyecto --no-launch-profile -- --crear-enlaces-fese-dev
    if ($LASTEXITCODE -ne 0) { throw 'No se completaron las cuentas. Revise el registro y las conexiones de desarrollo.' }
} finally {
    $env:FESE_ENLACES_PASSWORD = $passwordAnterior
    $env:ASPNETCORE_ENVIRONMENT = $ambienteAnterior
    $env:DOTNET_ENVIRONMENT = $ambienteDotnetAnterior
    $clave.Dispose()
}
