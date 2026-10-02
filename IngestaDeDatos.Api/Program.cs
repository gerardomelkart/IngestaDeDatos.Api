using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Data.SqlClient;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<DbFactory>();
builder.Services.AddScoped<UsuariosService>();
builder.Services.AddScoped<DdcpService>();
builder.Services.AddControllers();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? []).AllowAnyHeader().AllowAnyMethod()));
if (args.Contains("--crear-superusuario"))
{
    await new UsuariosService(new DbFactory(builder.Configuration)).Bootstrap();
    Console.WriteLine("Superusuario creado correctamente.");
    return;
}
var secret = builder.Configuration["Jwt:SecretKey"] ?? "";
if (Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("Defina Jwt:SecretKey con al menos 32 bytes mediante user-secrets o variable Jwt__SecretKey.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true, ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidAudience = builder.Configuration["Jwt:Audience"], IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), ClockSkew = TimeSpan.FromSeconds(30) });
builder.Services.AddAuthorization();
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (Exception ex)
    {
        var status = ex is UnauthorizedAccessException ? 403 : ex is ArgumentException ? 400 : ex is SqlException sql && sql.Number is 2601 or 2627 ? 409 : 500;
        if (status == 500) app.Logger.LogError(ex, "Error de operación.");
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { mensaje = status == 500 ? "No fue posible completar la operación. Revise el log de la API." : status == 409 ? "Ya existe un registro con esa clave. Actualice la vista y vuelva a intentar." : ex.Message });
    }
});
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/", () => new { sistema = "IngestaDeDatos.Api", estado = "OK" });
app.MapControllers();
app.Run();
