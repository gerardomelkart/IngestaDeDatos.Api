using Dapper;
using IngestaDeDatos.Api.Configuration;
using IngestaDeDatos.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using System.Data;
using System.Security.Claims;
using System.Text;

namespace IngestaDeDatos.Api.Fese;

public sealed class PasswordFese(DbFactory factory, IHostEnvironment ambiente, IOptions<ReglasDdcpOptions> reglas)
{
    public static bool DebeCambiar(bool desarrollo, bool marcado) => !desarrollo && marcado;
    public async Task<bool> Requiere(int id)
    {
        if (ambiente.IsDevelopment()) return false;
        await using var db = factory.Modulo("FESE");
        return DebeCambiar(ambiente.IsDevelopment(), await db.ExecuteScalarAsync<bool>("SELECT COALESCE((SELECT REQUIERE_CAMBIO FROM dbo.FESE_CREDENCIAL WHERE ID_USUARIO=@id),0)", new { id }));
    }
    public static string? ValidarNueva(string nueva, string hashActual, ReglasDdcpOptions reglas)
    {
        if (string.IsNullOrEmpty(nueva) || nueva.Length < reglas.PasswordMinimo || Encoding.UTF8.GetByteCount(nueva) > reglas.PasswordMaximoBytes)
            return $"La nueva contraseña requiere al menos {reglas.PasswordMinimo} caracteres y un máximo de {reglas.PasswordMaximoBytes} bytes.";
        if (BCrypt.Net.BCrypt.Verify(nueva, hashActual)) return "La nueva contraseña debe ser distinta de la actual.";
        return null;
    }
    public async Task<string?> Cambiar(int id, string actual, string nueva)
    {
        if (string.IsNullOrEmpty(actual) || Encoding.UTF8.GetByteCount(actual) > reglas.Value.PasswordMaximoBytes)
            return "Escriba su contraseña actual.";
        await using var db = factory.Auth();
        await using var fese = factory.Modulo("FESE");
        if (!string.Equals(db.DataSource, fese.DataSource, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Autenticación y FESE deben estar en el mismo servidor para actualizar la credencial de forma atómica.");
        var baseFese = fese.Database; // DbFactory permite únicamente FESE / FESE_DEV.
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var hash = await db.QuerySingleOrDefaultAsync<string>("SELECT PASSWORD_HASH FROM dbo.ING_USUARIO WITH (UPDLOCK,HOLDLOCK) WHERE ID_USUARIO=@id AND HABILITADO=1", new { id }, tx);
        if (hash is null || !BCrypt.Net.BCrypt.Verify(actual, hash)) return "La contraseña actual es incorrecta.";
        var error = ValidarNueva(nueva, hash, reglas.Value);
        if (error is not null) return error;
        await db.ExecuteAsync("UPDATE dbo.ING_USUARIO SET PASSWORD_HASH=@hash WHERE ID_USUARIO=@id", new { id, hash = BCrypt.Net.BCrypt.HashPassword(nueva, reglas.Value.BCryptWorkFactor) }, tx);
        await db.ExecuteAsync($"UPDATE [{baseFese}].dbo.FESE_CREDENCIAL SET REQUIERE_CAMBIO=0,FECHA_CAMBIO=@fecha WHERE ID_USUARIO=@id", new { id, fecha = RepositorioFese.Ahora() }, tx);
        await tx.CommitAsync();
        return null;
    }
}

public sealed class PasswordFeseFilter(PasswordFese password) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var accion = (context.ActionDescriptor as ControllerActionDescriptor)?.ActionName;
        if (accion is not ("Catalogos" or "CambiarPassword") &&
            int.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) &&
            await password.Requiere(id))
        {
            context.Result = new ObjectResult(new { codigo = "CAMBIO_PASSWORD_REQUERIDO", mensaje = "Debe cambiar su contraseña inicial antes de utilizar FESE." }) { StatusCode = 403 };
            return;
        }
        await next();
    }
}
