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
    private readonly IHttpContextAccessor? requests;
    private readonly FileState ddcp = new("ddcp");
    private readonly FileState fese = new("fese");
    private bool disposed;

    public FileLogProvider(IConfiguration configuration, string contentRoot, IHttpContextAccessor? requests = null)
    {
        this.requests = requests;
        var configuredDirectory = configuration["Logs:Directory"] ?? throw new InvalidOperationException("Falta Logs:Directory.");
        directory = Path.GetFullPath(configuredDirectory, contentRoot);
        maximumBytes = configuration.GetValue<long>("Logs:MaxFileSizeMB") * 1024 * 1024;
        retentionDays = configuration.GetValue<int>("Logs:RetentionDays");
        if (maximumBytes <= 0 || retentionDays <= 0) throw new InvalidOperationException("Configure tamaño y retención de logs positivos.");
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
        var path = requests?.HttpContext?.Request.Path;
        var file = path?.StartsWithSegments("/api/fese") == true
            || category.StartsWith("IngestaDeDatos.Api.Fese.", StringComparison.Ordinal)
            ? fese : ddcp;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            try
            {
                var day = DateOnly.FromDateTime(now.UtcDateTime);
                if (file.Writer is null || day != file.Day || file.Bytes + bytes > maximumBytes)
                {
                    OpenFile(file, day, bytes);
                }
                file.Writer!.WriteLine(line);
                file.Writer.Flush();
                file.Bytes += bytes;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                file.Writer?.Dispose();
                file.Writer = null;
                Console.Error.WriteLine($"No se pudo escribir el log ({error.GetType().Name}). Evento: {line}");
            }
        }
    }

    private void OpenFile(FileState file, DateOnly day, int incomingBytes)
    {
        file.Writer?.Dispose();
        file.Writer = null;
        if (file.Day != day)
        {
            file.Day = day;
            file.Sequence = 0;
            RemoveExpiredFiles(file.Prefix, day);
        }
        while (true)
        {
            var path = Path.Combine(directory, $"{file.Prefix}-{day:yyyy-MM-dd}-{file.Sequence:D3}.log");
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            if (stream.Length == 0 || stream.Length + incomingBytes <= maximumBytes)
            {
                file.Bytes = stream.Length;
                stream.Seek(0, SeekOrigin.End);
                file.Writer = new StreamWriter(stream, new UTF8Encoding(false));
                return;
            }
            stream.Dispose();
            file.Sequence++;
        }
    }

    private void RemoveExpiredFiles(string prefix, DateOnly today)
    {
        foreach (var path in Directory.EnumerateFiles(directory, $"{prefix}-*.log"))
        {
            var name = Path.GetFileName(path);
            if (name.Length < prefix.Length + 15 || !DateOnly.TryParseExact(name.Substring(prefix.Length + 1, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) || day > today.AddDays(-retentionDays))
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
            ddcp.Writer?.Dispose();
            fese.Writer?.Dispose();
        }
    }

    private sealed class FileState(string prefix)
    {
        public string Prefix { get; } = prefix;
        public StreamWriter? Writer { get; set; }
        public DateOnly Day { get; set; }
        public long Bytes { get; set; }
        public int Sequence { get; set; }
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

