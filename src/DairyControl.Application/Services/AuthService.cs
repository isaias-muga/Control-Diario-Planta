using DairyControl.Application.DTOs;
using DairyControl.Application.Seguridad;
using DairyControl.Application.Settings;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace DairyControl.Application.Services
{
    /// <summary>The state of a valid session, as the API needs it to authorize a request.</summary>
    public sealed record SesionValida(bool DebeCambiarPassword);

    public class AuthService
    {
        public const string ClaimRol = "role";
        public const string ClaimSello = "stamp";
        public const string ClaimNombre = "name";
        public const string ClaimDebeCambiarPassword = "dcp";

        // A hash to verify against when the user does not exist or is not active, so a login takes about the same time either way.
        private static string? _hashDeRelleno;

        private readonly IOptions<JwtSettings> _jwtSettings;
        private readonly IUsuarioRepository _usuarios;
        private readonly IHasherDePassword _hasher;
        private readonly IUsuarioActual _actual;
        private readonly TimeProvider _reloj;

        public AuthService(IOptions<JwtSettings> jwtSettings, IUsuarioRepository usuarios, IHasherDePassword hasher, IUsuarioActual actual, TimeProvider reloj)
        {
            _jwtSettings = jwtSettings;
            _usuarios = usuarios;
            _hasher = hasher;
            _actual = actual;
            _reloj = reloj;
        }

        /// <summary>Returns the session token, or null when the credentials are wrong. A locked user and an unknown user look the same.</summary>
        public async Task<LoginResultadoDto?> LoginAsync(LoginDto dto)
        {
            var ahora = _reloj.GetUtcNow().UtcDateTime;
            var usuario = await _usuarios.GetByUsernameAsync((dto.Username ?? "").Trim());

            if (usuario is null || !usuario.Activo)
            {
                _hasher.Verify(HashDeRelleno(), dto.Password ?? "");
                return null;
            }

            var correcta = _hasher.Verify(usuario.PasswordHash, dto.Password ?? "");
            if (usuario.LoginBloqueado(ahora))
            {
                return null;
            }

            if (!correcta)
            {
                usuario.RegistrarFalloLogin(ahora);
                await _usuarios.UpdateAsync(usuario);
                return null;
            }

            if (usuario.FallosLogin > 0 || usuario.LoginBloqueadoHastaUtc is not null)
            {
                usuario.ReiniciarFallosLogin();
                await _usuarios.UpdateAsync(usuario);
            }
            return Resultado(usuario, ahora);
        }

        /// <summary>A fresh token for the current user (sliding session); null when the user is no longer active.</summary>
        public async Task<LoginResultadoDto?> RenovarAsync()
        {
            var usuario = await UsuarioActualAsync();
            return usuario is null ? null : Resultado(usuario, _reloj.GetUtcNow().UtcDateTime);
        }

        /// <summary>The user chooses a new password (also the one forced after a reset). The old sessions end, so a fresh token is returned.</summary>
        public async Task<LoginResultadoDto?> CambiarPasswordAsync(CambiarPasswordDto dto)
        {
            var usuario = await UsuarioActualAsync();
            if (usuario is null)
            {
                return null;
            }
            var ahora = _reloj.GetUtcNow().UtcDateTime;
            if (usuario.LoginBloqueado(ahora))
            {
                throw new DemasiadosIntentosException("Demasiados intentos. Probá de nuevo en unos minutos.");
            }

            if (!_hasher.Verify(usuario.PasswordHash, dto.PasswordActual ?? ""))
            {
                usuario.RegistrarFalloLogin(ahora);
                await _usuarios.UpdateAsync(usuario);
                throw new ReglaDeNegocioException("La contraseña actual no es correcta.", "passwordActual");
            }
            Usuario.ValidarPassword(dto.PasswordNueva, "passwordNueva");
            if (dto.PasswordNueva == dto.PasswordActual)
            {
                throw new ReglaDeNegocioException("La contraseña nueva debe ser distinta de la actual.", "passwordNueva");
            }

            usuario.CambiarPassword(_hasher.Hash(dto.PasswordNueva));
            await _usuarios.UpdateAsync(usuario);
            return Resultado(usuario, ahora);
        }

        /// <summary>Checked on every request: the user must exist, be active and carry the current security stamp.</summary>
        public async Task<SesionValida?> ValidarSesionAsync(Guid usuarioId, Guid sello)
        {
            var usuario = await _usuarios.GetByIdAsync(usuarioId);
            if (usuario is null || !usuario.Activo || usuario.SecurityStamp != sello)
            {
                return null;
            }
            return new SesionValida(usuario.DebeCambiarPassword);
        }

        private async Task<Usuario?> UsuarioActualAsync()
        {
            if (_actual.Id is not Guid id)
            {
                return null;
            }
            var usuario = await _usuarios.GetByIdAsync(id);
            return usuario is { Activo: true } ? usuario : null;
        }

        private string HashDeRelleno() => _hashDeRelleno ??= _hasher.Hash("relleno-para-igualar-tiempos");

        private LoginResultadoDto Resultado(Usuario usuario, DateTime ahora) => new()
        {
            Token = CrearToken(usuario, ahora),
            DebeCambiarPassword = usuario.DebeCambiarPassword
        };

        private string CrearToken(Usuario usuario, DateTime ahora)
        {
            var settings = _jwtSettings.Value;
            var credenciales = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)), SecurityAlgorithms.HmacSha256);
            var duracion = usuario.Rol == RolUsuario.Recepcion ? TimeSpan.FromDays(settings.RecepcionDias) : TimeSpan.FromHours(settings.PersonalHoras);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
                new(ClaimRol, usuario.Rol.ToString()),
                new(ClaimSello, usuario.SecurityStamp.ToString()),
                new(ClaimNombre, usuario.Nombre)
            };
            if (usuario.DebeCambiarPassword)
            {
                claims.Add(new Claim(ClaimDebeCambiarPassword, "1"));
            }

            // The handler stamps nbf/iat with the system clock unless told otherwise: every date comes from one clock
            var descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                NotBefore = ahora,
                IssuedAt = ahora,
                Expires = ahora + duracion,
                Issuer = settings.Issuer,
                Audience = settings.Audience,
                SigningCredentials = credenciales
            };

            var handler = new JwtSecurityTokenHandler();
            return handler.WriteToken(handler.CreateToken(descriptor));
        }
    }
}
