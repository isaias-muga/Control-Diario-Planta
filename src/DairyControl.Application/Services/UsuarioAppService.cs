using DairyControl.Application.DTOs;
using DairyControl.Application.Seguridad;
using DairyControl.Application.Settings;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace DairyControl.Application.Services
{
    public class UsuarioAppService
    {
        private readonly IUsuarioRepository _usuarios;
        private readonly IHasherDePassword _hasher;
        private readonly IUsuarioActual _actual;
        private readonly IOptions<AdminUserSettings> _adminInicial;

        public UsuarioAppService(IUsuarioRepository usuarios, IHasherDePassword hasher, IUsuarioActual actual, IOptions<AdminUserSettings> adminInicial)
        {
            _usuarios = usuarios;
            _hasher = hasher;
            _actual = actual;
            _adminInicial = adminInicial;
        }

        public async Task<IReadOnlyList<UsuarioDto>> GetAllAsync()
        {
            var usuarios = await _usuarios.GetAllAsync();
            return usuarios.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase).Select(ADto).ToList();
        }

        public async Task<UsuarioDto> CrearAsync(CrearUsuarioDto dto)
        {
            if (!Enum.TryParse<RolUsuario>(dto.Rol, ignoreCase: true, out var rol) || !Enum.IsDefined(rol))
            {
                throw new ReglaDeNegocioException("El rol no es válido.", "rol");
            }
            Usuario.ValidarPassword(dto.Password);

            var usuario = Usuario.Crear(dto.Username, dto.Nombre, rol, _hasher.Hash(dto.Password));
            if (await _usuarios.GetByUsernameAsync(usuario.Username) is not null)
            {
                throw new ConflictoDeUnicidadException("Ya existe un usuario con ese nombre de usuario.");
            }
            await _usuarios.AddAsync(usuario);
            return ADto(usuario);
        }

        /// <summary>Ends the user's sessions at once. Returns false when the user does not exist.</summary>
        public async Task<bool> DesactivarAsync(Guid id)
        {
            var usuario = await _usuarios.GetByIdAsync(id);
            if (usuario is null)
            {
                return false;
            }
            if (id == _actual.Id)
            {
                throw new ReglaDeNegocioException("No podés desactivar tu propio usuario.", "id");
            }
            usuario.Desactivar();
            await _usuarios.UpdateAsync(usuario);
            return true;
        }

        /// <summary>Sets a temporary password; the user has to choose another one at the next login. Returns false when the user does not exist.</summary>
        public async Task<bool> RestablecerPasswordAsync(Guid id, RestablecerPasswordDto dto)
        {
            var usuario = await _usuarios.GetByIdAsync(id);
            if (usuario is null)
            {
                return false;
            }
            Usuario.ValidarPassword(dto.Password);
            usuario.RestablecerPassword(_hasher.Hash(dto.Password));
            await _usuarios.UpdateAsync(usuario);
            return true;
        }

        /// <summary>
        /// When there are no users yet, creates the first Admin from the deployment secret (AdminUser:Password). Returns true if it created it.
        /// That password is chosen by whoever deploys, so the admin is not forced to change it.
        /// </summary>
        public async Task<bool> AsegurarAdminInicialAsync()
        {
            if (await _usuarios.AnyAsync())
            {
                return false;
            }
            var ajustes = _adminInicial.Value;
            if (string.IsNullOrEmpty(ajustes.Password))
            {
                throw new InvalidOperationException("No hay usuarios y falta AdminUser:Password para crear el primer administrador.");
            }
            Usuario.ValidarPassword(ajustes.Password, "AdminUser:Password");

            var admin = Usuario.Crear(ajustes.Username, "Administrador", RolUsuario.Admin, _hasher.Hash(ajustes.Password), debeCambiarPassword: false);
            await _usuarios.AddAsync(admin);
            return true;
        }

        private static UsuarioDto ADto(Usuario usuario) => new()
        {
            Id = usuario.Id,
            Username = usuario.Username,
            Nombre = usuario.Nombre,
            Rol = usuario.Rol.ToString(),
            Activo = usuario.Activo,
            DebeCambiarPassword = usuario.DebeCambiarPassword
        };
    }
}
