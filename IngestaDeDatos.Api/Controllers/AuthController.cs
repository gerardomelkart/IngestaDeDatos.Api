using IngestaDeDatos.Api.Models;
using IngestaDeDatos.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace IngestaDeDatos.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(UsuariosService usuarios, IConfiguration configuration) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Usuario) || string.IsNullOrEmpty(request.Password) || Encoding.UTF8.GetByteCount(request.Password) > 72)
        {
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." }); 
        }

        var usuario = await usuarios.Buscar(request.Usuario.Trim());
        if (usuario is null || !BCrypt.Net.BCrypt.Verify(request.Password, usuario.PasswordHash))
        { 
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." }); 
        }

        var claims = new[] 
        {
            new Claim(ClaimTypes.NameIdentifier, usuario.IdUsuario.ToString()), new Claim(ClaimTypes.Name, usuario.Usuario), new Claim(ClaimTypes.Role, usuario.Rol) 
        };

        var expires = DateTime.UtcNow.AddMinutes(configuration.GetValue<int>("Jwt:MinutesToExpire", 120));
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:SecretKey"]!)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(configuration["Jwt:Issuer"], configuration["Jwt:Audience"], claims, expires: expires, signingCredentials: credentials);

        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), expira = expires, usuario = new { usuario.IdUsuario, usuario.Usuario, usuario.Nombre, usuario.Rol, usuario.EsNacional, usuario.IdEntidad } });
    }
}
