using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Logging;
using IngestaDeDatos.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
using var fileLogs = new FileLogProvider(builder.Configuration, builder.Environment.ContentRootPath);
builder.Logging.AddProvider(fileLogs);
builder.Logging.AddFilter<FileLogProvider>((category, level) => category?.StartsWith("IngestaDeDatos.Api", StringComparison.Ordinal) == true && level >= LogLevel.Information);
using var startupFactory = LoggerFactory.Create(logging => logging.AddConsole().AddProvider(fileLogs));
var startup = startupFactory.CreateLogger("IngestaDeDatos.Api.Arranque");

try
{
    startup.LogInformation("Inicio del proceso. Entorno {Environment}; modo {Mode}.", builder.Environment.EnvironmentName, args.Contains("--crear-superusuario") ? "crear-superusuario" : "servidor");
    builder.Services.AddSingleton<DbFactory>();
    builder.Services.AddScoped<UsuariosService>();
    builder.Services.AddScoped<DdcpService>();
    builder.Services.AddControllers();
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? []).AllowAnyHeader().AllowAnyMethod()));

    if (args.Contains("--crear-superusuario"))
    {
        await new UsuariosService(new DbFactory(builder.Configuration)).Bootstrap();
        startup.LogInformation("Inicialización del superusuario completada.");
        Console.WriteLine("Superusuario creado correctamente.");
        return;
    }

    var secret = builder.Configuration["Jwt:SecretKey"] ?? "";
    if (Encoding.UTF8.GetByteCount(secret) < 32)
    {
        throw new InvalidOperationException("Defina Jwt:SecretKey con al menos 32 bytes mediante user-secrets o variable Jwt__SecretKey.");
    }
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true, ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidAudience = builder.Configuration["Jwt:Audience"], IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), ClockSkew = TimeSpan.FromSeconds(30) });
    builder.Services.AddAuthorization();

    await using var app = builder.Build();
    app.UseRouting();
    app.UseMiddleware<RequestLogMiddleware>();
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapGet("/", () => new { estado = "OK" });
    app.MapControllers();
    app.Lifetime.ApplicationStarted.Register(() => startup.LogInformation("Servicio iniciado."));
    app.Lifetime.ApplicationStopping.Register(() => startup.LogInformation("Detención del servicio solicitada."));
    app.Lifetime.ApplicationStopped.Register(() => startup.LogInformation("Servicio detenido."));
    await app.RunAsync();
}
catch (Exception exception)
{
    var sqlNumber = exception is Microsoft.Data.SqlClient.SqlException sql ? sql.Number : (int?)null;
    startup.LogCritical("El proceso finalizó por un error. Tipo {ExceptionType}; código SQL {SqlNumber}; pila {StackTrace}.", exception.GetType().FullName, sqlNumber, exception.StackTrace);
    Environment.ExitCode = 1;
    Console.Error.WriteLine("No fue posible iniciar o completar el proceso. Revise los logs y la configuración.");
}
