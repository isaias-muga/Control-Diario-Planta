using DairyControl.Application.DTOs;
using DairyControl.Application.Queries;
using DairyControl.Application.Seguridad;
using DairyControl.Application.Settings;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using DairyControl.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace DairyControl.Application.Services
{
    public class UsuarioAppService
    {
        private readonly IUsuarioRepository _usuarios;
        private readonly IHasherDePassword _hasher;
        private readonly IUsuarioActual _actual;
        private readonly IOptions<AdminUserSettings> _adminInicial;
        private readonly IHistorialQueries _historial;
        private readonly TimeProvider _reloj;

        public UsuarioAppService(IUsuarioRepository usuarios, IHasherDePassword hasher, IUsuarioActual actual, IOptions<AdminUserSettings> adminInicial,
            IHistorialQueries historial, TimeProvider reloj)
        {
            _usuarios = usuarios;
            _hasher = hasher;
            _actual = actual;
            _adminInicial = adminInicial;
            _historial = historial;
            _reloj = reloj;
        }

        public async Task<IReadOnlyList<UsuarioDto>> GetAllAsync()
        {
            var usuarios = await _usuarios.GetAllAsync();
            return usuarios.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase).Select(ADto).ToList();
        }

        public async Task<UsuarioDto> CrearAsync(CrearUsuarioDto dto)
        {
            var rol = ParsearRol(dto.Rol);
            Usuario.ValidarPassword(dto.Password);
            var actorId = ActorId();

            var usuario = Usuario.Crear(dto.Username, dto.Nombre, rol, _hasher.Hash(dto.Password));
            if (await _usuarios.GetByUsernameAsync(usuario.Username) is not null)
            {
                throw new ConflictoDeUnicidadException("Ya existe un usuario con ese nombre de usuario.");
            }
            await _usuarios.AddAsync(usuario, usuario.RegistrarAlta(actorId, Ahora()));
            return ADto(usuario);
        }

        /// <summary>
        /// Ends the user's sessions at once. A protected account is refused (403), and so is the last active Admin (400). Returns false when
        /// the user does not exist.
        /// </summary>
        public async Task<bool> DesactivarAsync(Guid id, MotivoCambioDto dto)
        {
            var motivo = MotivoDeCambio.Create(dto.Motivo);
            var actorId = ActorId();

            var usuario = await _usuarios.GetByIdAsync(id);
            if (usuario is null)
            {
                return false;
            }
            if (id == actorId)
            {
                throw new ReglaDeNegocioException("No podés desactivar tu propio usuario.", "id");
            }
            var eraAdminActivo = usuario.EsAdminActivo;
            var cambio = usuario.Desactivar(motivo, actorId, Ahora());
            await _usuarios.GuardarCambioAsync(usuario, cambio, exigirOtroAdminActivo: eraAdminActivo && !usuario.EsAdminActivo);
            return true;
        }

        /// <summary>The user can log in again, with new sessions only, and has to choose a password of their own. Returns false when the user does not exist.</summary>
        public async Task<bool> ReactivarAsync(Guid id, MotivoCambioDto dto)
        {
            var motivo = MotivoDeCambio.Create(dto.Motivo);
            var actorId = ActorId();

            var usuario = await _usuarios.GetByIdAsync(id);
            if (usuario is null)
            {
                return false;
            }
            var cambio = usuario.Reactivar(motivo, actorId, Ahora());
            await _usuarios.GuardarCambioAsync(usuario, cambio, exigirOtroAdminActivo: false);
            return true;
        }

        /// <summary>Changes the role (the sessions end). A protected account is refused (403), and so is demoting the last active Admin (400). Returns null when the user does not exist.</summary>
        public async Task<UsuarioDto?> CambiarRolAsync(Guid id, CambiarRolDto dto)
        {
            var rol = ParsearRol(dto.Rol);
            var motivo = MotivoDeCambio.Create(dto.Motivo);
            var actorId = ActorId();

            var usuario = await _usuarios.GetByIdAsync(id);
            if (usuario is null)
            {
                return null;
            }
            var eraAdminActivo = usuario.EsAdminActivo;
            var cambio = usuario.CambiarRol(rol, motivo, actorId, Ahora());
            await _usuarios.GuardarCambioAsync(usuario, cambio, exigirOtroAdminActivo: eraAdminActivo && !usuario.EsAdminActivo);
            return ADto(usuario);
        }

        /// <summary>Sets a temporary password; the user has to choose another one at the next login. A protected account is refused (403) unless it is the user's own. Returns false when the user does not exist.</summary>
        public async Task<bool> RestablecerPasswordAsync(Guid id, RestablecerPasswordDto dto)
        {
            var usuario = await _usuarios.GetByIdAsync(id);
            if (usuario is null)
            {
                return false;
            }
            var actorId = ActorId();
            usuario.ValidarModificablePor(actorId);
            Usuario.ValidarPassword(dto.Password);
            var cambio = usuario.RestablecerPassword(_hasher.Hash(dto.Password), actorId, Ahora());
            await _usuarios.GuardarCambioAsync(usuario, cambio, exigirOtroAdminActivo: false);
            return true;
        }

        /// <summary>Null when the user does not exist.</summary>
        public Task<IReadOnlyList<CorreccionDto>?> GetHistorialAsync(Guid id) =>
            _historial.GetAsync(CambioRegistrado.EntidadUsuario, id);

        /// <summary>
        /// When there are no users yet, creates the first Admin from the deployment secret (AdminUser:Password). Returns true if it created it.
        /// Unless AdminUser:ForzarCambioPassword is false, the admin must choose a password of their own at the first login.
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

            var admin = Usuario.Crear(ajustes.Username, "Administrador", RolUsuario.Admin, _hasher.Hash(ajustes.Password), debeCambiarPassword: ajustes.ForzarCambioPassword);
            admin.MarcarProtegido(true);
            await _usuarios.AddAsync(admin, admin.RegistrarAlta(admin.Id, Ahora(), "Alta inicial"));
            return true;
        }

        /// <summary>
        /// Makes the protected accounts match the configuration: the bootstrap admin (AdminUser:Username) and the usernames listed in
        /// AdminUser:Protegidos are protected, everyone else is not. Runs at every start; nothing else can set or clear the flag.
        /// Returns the configured usernames that no user has (a typo, or a user not created yet).
        /// </summary>
        public async Task<IReadOnlyList<string>> SincronizarProtegidosAsync()
        {
            var ajustes = _adminInicial.Value;
            var protegidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var nombre in (ajustes.Protegidos ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                protegidos.Add(nombre);
            }
            if (!string.IsNullOrWhiteSpace(ajustes.Username))
            {
                protegidos.Add(ajustes.Username.Trim());
            }

            var usuarios = await _usuarios.GetAllParaModificarAsync();
            var cambioAlgo = false;
            foreach (var usuario in usuarios)
            {
                cambioAlgo |= usuario.MarcarProtegido(protegidos.Contains(usuario.Username));
            }
            if (cambioAlgo)
            {
                await _usuarios.UpdateAsync(usuarios[0]);
            }

            return protegidos.Where(nombre => !usuarios.Any(u => string.Equals(u.Username, nombre, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        private DateTime Ahora() => _reloj.GetUtcNow().UtcDateTime;

        private Guid ActorId() =>
            _actual.Id ?? throw new InvalidOperationException("No hay un usuario autenticado en la solicitud.");

        private static RolUsuario ParsearRol(string? texto)
        {
            // By name only: Enum.TryParse would also take "3" and map it to a role by its number
            if (texto is null || !Enum.GetNames<RolUsuario>().Contains(texto, StringComparer.OrdinalIgnoreCase)
                || !Enum.TryParse<RolUsuario>(texto, ignoreCase: true, out var rol))
            {
                throw new ReglaDeNegocioException("El rol no es válido.", "rol");
            }
            return rol;
        }

        private static UsuarioDto ADto(Usuario usuario) => new()
        {
            Id = usuario.Id,
            Username = usuario.Username,
            Nombre = usuario.Nombre,
            Rol = usuario.Rol.ToString(),
            Activo = usuario.Activo,
            DebeCambiarPassword = usuario.DebeCambiarPassword,
            Protegido = usuario.Protegido
        };
    }
}
