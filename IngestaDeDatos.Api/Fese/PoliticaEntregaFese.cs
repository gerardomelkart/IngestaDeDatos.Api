namespace IngestaDeDatos.Api.Fese;

public static class PoliticaEntregaFese
{
    public static DateTime Corte(DateTime hoy) => new DateTime(hoy.Year, hoy.Month, 1).AddMonths(-1);

    public static void Validar(DateTime periodo, bool actualizacion, bool existeEntrega, DateTime hoy)
    {
        var corte = Corte(hoy);
        if (periodo > corte)
            throw new ArgumentException("Solo se reciben periodos cerrados hasta el corte del mes anterior.");
        if (actualizacion)
        {
            if (!existeEntrega)
                throw new ArgumentException("Seleccione una entrega aceptada para actualizar. La primera entrega se realiza en Cargar información.");
        }
        else
        {
            if (periodo != corte)
                throw new ArgumentException($"La carga inicial corresponde al corte {corte:yyyy-MM}. No puede elegirse otro periodo.");
            if (existeEntrega)
                throw new ArgumentException("Este periodo ya tiene una entrega aceptada. Utilice Actualizaciones para sustituirla.");
        }
    }
}
