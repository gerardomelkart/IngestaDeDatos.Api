using IngestaDeDatos.Api.Models;

namespace IngestaDeDatos.Api.Validators;

public static class UsuarioValidator
{
    public static void Validar(UsuarioRequest request, bool nuevo)
    {
        if (string.IsNullOrWhiteSpace(request.Usuario) || request.Usuario.Trim().Length > 80 || string.IsNullOrWhiteSpace(request.Nombre) || request.Nombre.Trim().Length > 160)
        {
            throw new ArgumentException("Capture usuario (máximo 80 caracteres) y nombre (máximo 160).");
        }
        if ((nuevo || !string.IsNullOrEmpty(request.Password)) && (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8 || System.Text.Encoding.UTF8.GetByteCount(request.Password) > 72))
        {
            throw new ArgumentException("La contraseña requiere al menos 8 caracteres y un máximo de 72 bytes.");
        }
        if (!new[] { "SUPER_USUARIO", "ENLACE_ESTATAL", "CONSULTA" }.Contains(request.Rol))
        {
            throw new ArgumentException("Rol inválido.");
        }
        if (request.Rol == "SUPER_USUARIO" && !request.EsNacional)
        {
            throw new ArgumentException("El superusuario siempre debe ser nacional.");
        }
        if (request.EsNacional == request.IdEntidad.HasValue)
        {
            throw new ArgumentException("El usuario nacional no lleva entidad; el usuario por entidad requiere una.");
        }
    }
}
