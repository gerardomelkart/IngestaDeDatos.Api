using IngestaDeDatos.Api.Configuration;

using IngestaDeDatos.Api.Models;

namespace IngestaDeDatos.Api.Validators;

public static class UsuarioValidator
{
    public static void Validar(UsuarioRequest request, bool nuevo, ReglasDdcpOptions reglas, RolCatalogo rol)
    {
        if (string.IsNullOrWhiteSpace(request.Usuario) || request.Usuario.Trim().Length > reglas.UsuarioMaximo || string.IsNullOrWhiteSpace(request.Nombre) || request.Nombre.Trim().Length > reglas.NombreMaximo)
        {
            throw new ArgumentException($"Capture usuario (máximo {reglas.UsuarioMaximo} caracteres) y nombre (máximo {reglas.NombreMaximo}).");
        }
        if ((nuevo || !string.IsNullOrEmpty(request.Password)) && (string.IsNullOrEmpty(request.Password) || request.Password.Length < reglas.PasswordMinimo || System.Text.Encoding.UTF8.GetByteCount(request.Password) > reglas.PasswordMaximoBytes))
        {
            throw new ArgumentException($"La contraseña requiere al menos {reglas.PasswordMinimo} caracteres y un máximo de {reglas.PasswordMaximoBytes} bytes.");
        }
        if (rol.NacionalObligatorio && !request.EsNacional)
        {
            throw new ArgumentException("El rol seleccionado requiere alcance nacional.");
        }
        if (request.EsNacional == request.IdEntidad.HasValue)
        {
            throw new ArgumentException("El usuario nacional no lleva entidad; el usuario por entidad requiere una.");
        }
    }
}

