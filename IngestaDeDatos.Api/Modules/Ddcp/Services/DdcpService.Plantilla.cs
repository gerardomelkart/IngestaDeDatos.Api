using ClosedXML.Excel;
using Dapper;
using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Models;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IngestaDeDatos.Api.Services;

public sealed partial class DdcpService
{    public async Task<byte[]> Plantilla(UsuarioInfo usuario, int anio, int mes)
    {
        PeriodoValido(anio, mes);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(CultureInfo.GetCultureInfo("es-MX").DateTimeFormat.GetMonthName(mes));
        sheet.Cell(1, 1).Value = "INFORME DE PUESTA DE DISPOSITIVOS A DISPOSICIÓN";
        sheet.Range(1, 1, 1, 4).Merge();
        sheet.Cell(2, 1).Value = $"01 AL {DateTime.DaysInMonth(anio, mes)} DE {Meses[mes]} {anio}";
        sheet.Range(2, 1, 2, 4).Merge();
        string[] columnas = ["Núm.", "Entidad Federativa", "Número de dispositivos decomisados", "Personas puestas a disposición"];
        for (var c = 0; c < columnas.Length; c++) sheet.Cell(4, c + 1).Value = columnas[c];
        var entidades = await Entidades(usuario);
        for (var i = 0; i < entidades.Count; i++) { sheet.Cell(i + 5, 1).Value = i + 1; sheet.Cell(i + 5, 2).Value = entidades[i].NombreEntidad; }
        sheet.Columns(1, 4).Width = 28;
        sheet.Column(2).Width = 30;
        sheet.Row(1).Height = 36;
        sheet.Row(4).Height = 36;
        sheet.Range(1, 1, entidades.Count + 4, 4).Style.Alignment.WrapText = true;
        sheet.Range(4, 1, 4, 4).Style.Font.Bold = true;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

}
