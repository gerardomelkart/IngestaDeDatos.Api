using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace IngestaDeDatos.Api.Fese;

internal sealed record FilaRdsFese(string Codigo, string Procedencia, string Region,
    string Estado, string Centro, string Tipo, string Incidente, long Total, DateTime Periodo, string? MesLargo, string? FechaOriginal);

internal static class ExtractoRdsFese
{
    public static string? BuscarEjecutable()
    {
        var configurado = Environment.GetEnvironmentVariable("FESE_RSCRIPT");
        if (!string.IsNullOrWhiteSpace(configurado))
            return Path.IsPathFullyQualified(configurado) && File.Exists(configurado) ? configurado : null;

        var nombre = OperatingSystem.IsWindows() ? "Rscript.exe" : "Rscript";
        foreach (var carpeta in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (!Path.IsPathFullyQualified(carpeta)) continue;
            var ruta = Path.Combine(carpeta, nombre);
            if (File.Exists(ruta)) return ruta;
        }

        if (!OperatingSystem.IsWindows()) return null;
        var instalaciones = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "R");
        if (!Directory.Exists(instalaciones)) return null;
        return Directory.EnumerateDirectories(instalaciones, "R-*")
            .OrderByDescending(ruta => Version.TryParse(Path.GetFileName(ruta)[2..], out var version) ? version : new Version())
            .Select(ruta => Path.Combine(ruta, "bin", nombre))
            .FirstOrDefault(File.Exists);
    }

    public static async Task<byte[]> Generar(IEnumerable<FilaRdsFese> filas, string ejecutable, CancellationToken cancelacion)
    {
        var carpeta = Directory.CreateTempSubdirectory("fese-rds-");
        try
        {
            var entrada = Path.Combine(carpeta.FullName, "datos.csv");
            var salida = Path.Combine(carpeta.FullName, "datos.rds");
            using (var csv = new StreamWriter(entrada, false, new UTF8Encoding(false)))
            {
                await csv.WriteLineAsync("codigo,ao,procedencia,region,estado,centro,tipo,incidente,total,mes,mes_largo,fecha");
                var cultura = CultureInfo.GetCultureInfo("es-MX");
                foreach (var fila in filas)
                {
                    cancelacion.ThrowIfCancellationRequested();
                    var mes = cultura.TextInfo.ToTitleCase(cultura.DateTimeFormat.GetMonthName(fila.Periodo.Month));
                    await csv.WriteLineAsync(string.Join(",", new[]
                    {
                        fila.Codigo, fila.Periodo.Year.ToString(CultureInfo.InvariantCulture), fila.Procedencia,
                        fila.Region, fila.Estado, fila.Centro, fila.Tipo, fila.Incidente,
                        fila.Total.ToString(CultureInfo.InvariantCulture), fila.Periodo.Month.ToString(CultureInfo.InvariantCulture),
                        fila.MesLargo ?? mes, fila.FechaOriginal ?? fila.Periodo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    }.Select((valor, indice) => indice is 1 or 8 or 9 ? valor : "\"" + valor.Replace("\"", "\"\"") + "\"")));
                }
            }

            var script = Path.Combine(carpeta.FullName, "generar.R");
            await File.WriteAllTextAsync(script, """
                args <- commandArgs(trailingOnly = TRUE)
                fese <- tryCatch(
                  read.csv(args[1], encoding = "UTF-8", na.strings = character(),
                    colClasses = c("character", "numeric", rep("character", 6),
                                   "numeric", "numeric", "character", "character")),
                  error = function(e) quit(status = 20L))
                attr(fese, "datalabel") <- ""
                attr(fese, "var.labels") <- rep("", ncol(fese))
                tryCatch(saveRDS(fese, args[2], version = 2),
                         error = function(e) quit(status = 21L))
                """, cancelacion);

            var inicio = new ProcessStartInfo(ejecutable)
            {
                UseShellExecute = false
            };
            foreach (var argumento in new[] { "--vanilla", script, entrada, salida })
                inicio.ArgumentList.Add(argumento);

            using var proceso = Process.Start(inicio) ?? throw new InvalidOperationException("No se pudo iniciar Rscript.");
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
            limite.CancelAfter(TimeSpan.FromMinutes(10));
            try
            {
                await proceso.WaitForExitAsync(limite.Token);
            }
            catch (OperationCanceledException)
            {
                if (!proceso.HasExited) proceso.Kill(entireProcessTree: true);
                await proceso.WaitForExitAsync();
                throw;
            }

            if (proceso.ExitCode != 0)
                throw new InvalidOperationException(proceso.ExitCode switch
                {
                    20 => "Rscript no pudo leer el CSV temporal de FESE.",
                    21 => "Rscript no pudo escribir el RDS temporal de FESE.",
                    _ => $"Rscript terminó con código {proceso.ExitCode}."
                });

            return await File.ReadAllBytesAsync(salida, cancelacion);
        }
        finally
        {
            carpeta.Delete(recursive: true);
        }
    }
}
