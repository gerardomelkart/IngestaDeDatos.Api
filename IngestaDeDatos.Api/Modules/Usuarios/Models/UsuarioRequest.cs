namespace IngestaDeDatos.Api.Models;

public record UsuarioRequest(string Usuario, string Nombre, string Password, string Rol, bool EsNacional, int? IdEntidad);
