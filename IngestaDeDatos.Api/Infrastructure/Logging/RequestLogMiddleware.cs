using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;

namespace IngestaDeDatos.Api.Logging;

public sealed class RequestLogMiddleware(RequestDelegate next, ILogger<RequestLogMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var timer = Stopwatch.StartNew();
        var requestId = context.TraceIdentifier;
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(sin ruta)";
        var method = context.Request.Method;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Request-Id"] = requestId;
            return Task.CompletedTask;
        });
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
            logger.LogWarning("Solicitud cancelada. Solicitud {RequestId}; operación {Method} {Route}.", requestId, method, route);
        }
        catch (Exception exception)
        {
            var status = exception is UnauthorizedAccessException ? 403 : exception is ArgumentException ? 400 : exception is SqlException sql && sql.Number is 2601 or 2627 ? 409 : 500;
            var sqlNumber = exception is SqlException sqlException ? sqlException.Number : (int?)null;
            if (status == 500 || context.Response.HasStarted)
            {
                logger.LogError("Error de operación. Solicitud {RequestId}; estado {Status}; código SQL {SqlNumber}; tipo {ExceptionType}; pila {StackTrace}.", requestId, status, sqlNumber, exception.GetType().FullName, exception.StackTrace);
            }
            else
            {
                logger.LogWarning("Operación rechazada. Solicitud {RequestId}; estado {Status}; tipo {ExceptionType}; código SQL {SqlNumber}.", requestId, status, exception.GetType().Name, sqlNumber);
            }
            if (context.Response.HasStarted)
            {
                throw;
            }
            context.Response.Clear();
            context.Response.StatusCode = status;
            var message = status == 500 ? "No fue posible completar la operación. Intente nuevamente o contacte al administrador del sistema." : status == 409 ? "Ya existe un registro con esa clave. Actualice la vista y vuelva a intentar." : exception.Message;
            await context.Response.WriteAsJsonAsync(new { mensaje = message }, context.RequestAborted);
        }
        finally
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anónimo";
            var status = context.Response.StatusCode;
            var level = status >= 500 ? LogLevel.Error : status >= 400 ? LogLevel.Warning : LogLevel.Information;
            logger.Log(level, "Solicitud {RequestId}; operación {Method} {Route}; estado {Status}; duración {ElapsedMs} ms; usuario {UserId}.", requestId, method, route, status, Math.Round(timer.Elapsed.TotalMilliseconds, 2), userId);
        }
    }
}
