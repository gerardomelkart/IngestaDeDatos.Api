using System.Globalization;
using System.Text;
using System.Text.Json;

namespace IngestaDeDatos.Api.Logging;

public sealed class FileLogProvider : ILoggerProvider
{
    private readonly object gate = new();
    private readonly string directory;
    private readonly long maximumBytes;
    private readonly int retentionDays;
    private StreamWriter? writer;
    private DateOnly currentDay;
    private long currentBytes;
    private int sequence;
    private bool disposed;

    public FileLogProvider(IConfiguration configuration, string contentRoot)
    {
        var configuredDirectory = configuration["Logs:Directory"] ?? "logs";
        directory = Path.GetFullPath(configuredDirectory, contentRoot);
        maximumBytes = Math.Clamp(configuration.GetValue<long>("Logs:MaxFileSizeMB", 10), 1, 100) * 1024 * 1024;
        retentionDays = Math.Clamp(configuration.GetValue<int>("Logs:RetentionDays", 30), 1, 365);
        Directory.CreateDirectory(directory);
        // Verifica escritura al arrancar, antes de aceptar solicitudes.
        var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
        using (File.Create(probe)) { }
        File.Delete(probe);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    private void Write(string category, LogLevel level, EventId eventId, object? state, string message, Exception? exception)
    {
        var properties = new Dictionary<string, object?>();
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            foreach (var value in values)
            {
                if (value.Key != "{OriginalFormat}")
                {
                    properties[value.Key] = value.Value;
                }
            }
        }
        // No se guardan mensajes de excepciones: pueden contener valores de entrada o secretos.
        var diagnostic = exception is null ? null : new
        {
            tipo = exception.GetType().FullName,
            pila = exception.StackTrace,
            tipoInterno = exception.InnerException?.GetType().FullName
        };
        var now = DateTimeOffset.UtcNow;
        var line = JsonSerializer.Serialize(new { fechaUtc = now, nivel = level.ToString(), categoria = category, evento = eventId.Id, mensaje = message, propiedades = properties, excepcion = diagnostic });
        var bytes = Encoding.UTF8.GetByteCount(line + Environment.NewLine);
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            try
            {
                var day = DateOnly.FromDateTime(now.UtcDateTime);
                if (writer is null || day != currentDay || currentBytes + bytes > maximumBytes)
                {
                    OpenFile(day, bytes);
                }
                writer!.WriteLine(line);
                writer.Flush();
                currentBytes += bytes;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                writer?.Dispose();
                writer = null;
                Console.Error.WriteLine($"No se pudo escribir el log ({error.GetType().Name}). Evento: {line}");
            }
        }
    }

    private void OpenFile(DateOnly day, int incomingBytes)
    {
        writer?.Dispose();
        writer = null;
        if (currentDay != day)
        {
            currentDay = day;
            sequence = 0;
            RemoveExpiredFiles(day);
        }
        while (true)
        {
            var path = Path.Combine(directory, $"ddcp-{day:yyyy-MM-dd}-{sequence:D3}.log");
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            if (stream.Length == 0 || stream.Length + incomingBytes <= maximumBytes)
            {
                currentBytes = stream.Length;
                stream.Seek(0, SeekOrigin.End);
                writer = new StreamWriter(stream, new UTF8Encoding(false));
                return;
            }
            stream.Dispose();
            sequence++;
        }
    }

    private void RemoveExpiredFiles(DateOnly today)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "ddcp-*.log"))
        {
            var name = Path.GetFileName(path);
            if (name.Length < 19 || !DateOnly.TryParseExact(name.Substring(5, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) || day > today.AddDays(-retentionDays))
            {
                continue;
            }
            try
            {
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"No se pudo eliminar un log vencido ({error.GetType().Name}).");
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            writer?.Dispose();
            writer = null;
        }
    }

    private sealed class FileLogger(FileLogProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(category, logLevel, eventId, state, formatter(state, exception), exception);
            }
        }
    }
}
