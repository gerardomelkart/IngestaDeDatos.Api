param([string]$Proyecto = (Join-Path $PSScriptRoot '..\IngestaDeDatos.Api\IngestaDeDatos.Api.csproj'))
$ErrorActionPreference = 'Stop'
# Reutiliza las credenciales YA configuradas. No imprime ni solicita contrasenas.
[xml]$csproj = Get-Content -LiteralPath $Proyecto -Raw
$id = [string]($csproj.Project.PropertyGroup.UserSecretsId | Where-Object { $_ } | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($id)) { throw 'El proyecto no tiene UserSecretsId.' }
$ruta = Join-Path $env:APPDATA "Microsoft\UserSecrets\$id\secrets.json"
if (!(Test-Path -LiteralPath $ruta)) { throw 'Configure primero los user-secrets locales de DDCP.' }
$secretos = Get-Content -LiteralPath $ruta -Raw | ConvertFrom-Json
$origen = $secretos.'ConnectionStrings:DDCP'
if (!$origen -and $secretos.ConnectionStrings) { $origen = $secretos.ConnectionStrings.DDCP }
if (!$origen) { throw 'Falta ConnectionStrings:DDCP en los secretos locales.' }
$cadena = New-Object System.Data.Common.DbConnectionStringBuilder
$cadena.set_ConnectionString([string]$origen)
$baseActual = if ($cadena.ContainsKey('Initial Catalog')) { $cadena.get_Item('Initial Catalog') } else { $cadena.get_Item('Database') }
if ($baseActual -ne 'DDCP_DEV') { throw "La conexion local DDCP apunta a [$baseActual]; se esperaba DDCP_DEV. No se hizo ningun cambio." }
[void]$cadena.Remove('Database')
$cadena.set_Item('Initial Catalog', 'FESE_DEV')
if ($secretos.ConnectionStrings)
{
    $secretos.ConnectionStrings | Add-Member -NotePropertyName 'FESE' -NotePropertyValue ($cadena.get_ConnectionString()) -Force
}
else
{
    $secretos | Add-Member -NotePropertyName 'ConnectionStrings:FESE' -NotePropertyValue ($cadena.get_ConnectionString()) -Force
}
$temporal = "$ruta.fese.tmp"
[IO.File]::WriteAllText($temporal, ($secretos | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false)))
Move-Item -LiteralPath $temporal -Destination $ruta -Force
Write-Host 'FESE_DEV configurada. Las credenciales no se imprimieron ni se guardaron en el repositorio.'
