using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Domain.Entities
{
    /// <summary>A person who logs in. The password is stored hashed (the hash is computed outside the domain).</summary>
    public class Usuario
    {
        public const int UsernameMinLength = 3;
        public const int UsernameMaxLength = 50;
        public const int NombreMaxLength = 100;
        public const int PasswordMinLength = 8;
        public const int PasswordMaxLength = 128;

        public const int MaxFallosLogin = 5;
        public static readonly TimeSpan BloqueoLogin = TimeSpan.FromMinutes(15);

        public const string MensajeUltimoAdmin = "Tiene que quedar al menos un administrador activo.";

        public const int MaxFallosPin = 5;
        public static readonly TimeSpan VentanaPin = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan BloqueoPin = TimeSpan.FromMinutes(5);

        public Guid Id { get; private set; }
        public string Username { get; private set; } = null!;
        public string Nombre { get; private set; } = null!;
        public RolUsuario Rol { get; private set; }
        public bool Activo { get; private set; }
        public string PasswordHash { get; private set; } = null!;

        /// <summary>Changes whenever the sessions must end (deactivation, password or role change); tokens carry it.</summary>
        public Guid SecurityStamp { get; private set; }

        /// <summary>True after the user was created or the password was reset by an admin: nothing else is allowed until the user chooses a new one.</summary>
        public bool DebeCambiarPassword { get; private set; }

        /// <summary>
        /// An owner account: no other Admin can deactivate it, demote it or reset its password. It is set only from configuration at startup
        /// (<see cref="MarcarProtegido"/>), never from an endpoint.
        /// </summary>
        public bool Protegido { get; private set; }

        /// <summary>An active Admin: the system always needs at least one.</summary>
        public bool EsAdminActivo => Activo && Rol == RolUsuario.Admin;

        public int FallosLogin { get; private set; }
        public DateTime? LoginBloqueadoHastaUtc { get; private set; }
        public int FallosPin { get; private set; }
        public DateTime? PrimerFalloPinUtc { get; private set; }
        public DateTime? PinBloqueadoHastaUtc { get; private set; }

        private Usuario() { }

        public static Usuario Crear(string username, string nombre, RolUsuario rol, string passwordHash, bool debeCambiarPassword = true)
        {
            var usernameTrim = ValidarUsername(username);
            var nombreTrim = ValidarNombre(nombre);
            ValidarRol(rol);
            ValidarHash(passwordHash);

            return new Usuario
            {
                Id = Guid.NewGuid(),
                Username = usernameTrim,
                Nombre = nombreTrim,
                Rol = rol,
                Activo = true,
                PasswordHash = passwordHash,
                SecurityStamp = Guid.NewGuid(),
                DebeCambiarPassword = debeCambiarPassword
            };
        }

        /// <summary>The rules for a new password, checked before it is hashed.</summary>
        public static void ValidarPassword(string? password, string campo = "password")
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ReglaDeNegocioException("La contraseña no puede estar vacía.", campo);
            }
            if (password.Length < PasswordMinLength)
            {
                throw new ReglaDeNegocioException($"La contraseña debe tener al menos {PasswordMinLength} caracteres.", campo);
            }
            if (password.Length > PasswordMaxLength)
            {
                throw new ReglaDeNegocioException($"La contraseña no puede superar los {PasswordMaxLength} caracteres.", campo);
            }
        }

        /// <summary>The history entry of the creation of this user (credited to whoever created it): the role it started with.</summary>
        public CambioRegistrado RegistrarAlta(Guid usuarioId, DateTime enUtc, string motivo = "Alta de usuario") =>
            CambioRegistrado.Crear(CambioRegistrado.EntidadUsuario, Id, Guid.NewGuid(), CambioRegistrado.Campos.Rol, null, Rol.ToString(), usuarioId, enUtc, MotivoDeCambio.Create(motivo));

        /// <summary>Only configuration decides who is an owner account. Returns true when the flag changed.</summary>
        public bool MarcarProtegido(bool protegido)
        {
            if (Protegido == protegido)
            {
                return false;
            }
            Protegido = protegido;
            return true;
        }

        /// <summary>A protected account can only be changed by its own user (anyone else gets <see cref="CuentaProtegidaException"/>).</summary>
        public void ValidarModificablePor(Guid actorId)
        {
            if (Protegido && actorId != Id)
            {
                throw new CuentaProtegidaException("Esta cuenta está protegida: otro administrador no puede desactivarla, cambiarle el rol ni restablecerle la contraseña.");
            }
        }

        /// <summary>Ends the sessions at once. Returns the history entry.</summary>
        public CambioRegistrado Desactivar(MotivoDeCambio motivo, Guid actorId, DateTime enUtc)
        {
            ArgumentNullException.ThrowIfNull(motivo);
            ValidarModificablePor(actorId);
            if (!Activo)
            {
                throw new ReglaDeNegocioException("El usuario ya está desactivado.", "id");
            }
            var cambio = CambioRegistrado.Crear(CambioRegistrado.EntidadUsuario, Id, Guid.NewGuid(), CambioRegistrado.Campos.Activo, "Activo", "Inactivo", actorId, enUtc, motivo);
            Activo = false;
            SecurityStamp = Guid.NewGuid();
            return cambio;
        }

        /// <summary>Back in with new sessions only (new stamp); the user must choose a password of their own at the next login.</summary>
        public CambioRegistrado Reactivar(MotivoDeCambio motivo, Guid actorId, DateTime enUtc)
        {
            ArgumentNullException.ThrowIfNull(motivo);
            if (Activo)
            {
                throw new ReglaDeNegocioException("El usuario ya está activo.", "id");
            }
            var cambio = CambioRegistrado.Crear(CambioRegistrado.EntidadUsuario, Id, Guid.NewGuid(), CambioRegistrado.Campos.Activo, "Inactivo", "Activo", actorId, enUtc, motivo);
            Activo = true;
            DebeCambiarPassword = true;
            SecurityStamp = Guid.NewGuid();
            ReiniciarFallosLogin();
            return cambio;
        }

        /// <summary>The user chose a new password: the old sessions end and the obligation to change it is over.</summary>
        public void CambiarPassword(string passwordHash)
        {
            ValidarHash(passwordHash);
            PasswordHash = passwordHash;
            DebeCambiarPassword = false;
            SecurityStamp = Guid.NewGuid();
            ReiniciarFallosLogin();
        }

        /// <summary>An admin set a temporary password: the user must choose another one at the next login.</summary>
        public CambioRegistrado RestablecerPassword(string passwordHash, Guid actorId, DateTime enUtc)
        {
            ValidarModificablePor(actorId);
            ValidarHash(passwordHash);
            // The history says that it happened, never the password or its hash
            var cambio = CambioRegistrado.Crear(CambioRegistrado.EntidadUsuario, Id, Guid.NewGuid(), CambioRegistrado.Campos.Password, null, "Restablecida", actorId, enUtc, MotivoDeCambio.Create("Contraseña restablecida"));
            PasswordHash = passwordHash;
            DebeCambiarPassword = true;
            SecurityStamp = Guid.NewGuid();
            ReiniciarFallosLogin();
            return cambio;
        }

        public CambioRegistrado CambiarRol(RolUsuario rol, MotivoDeCambio motivo, Guid actorId, DateTime enUtc)
        {
            ArgumentNullException.ThrowIfNull(motivo);
            ValidarModificablePor(actorId);
            ValidarRol(rol);
            if (rol == Rol)
            {
                throw new ReglaDeNegocioException("El usuario ya tiene ese rol.", nameof(rol));
            }
            var cambio = CambioRegistrado.Crear(CambioRegistrado.EntidadUsuario, Id, Guid.NewGuid(), CambioRegistrado.Campos.Rol, Rol.ToString(), rol.ToString(), actorId, enUtc, motivo);
            Rol = rol;
            SecurityStamp = Guid.NewGuid();
            return cambio;
        }

        public bool LoginBloqueado(DateTime ahoraUtc) => LoginBloqueadoHastaUtc > ahoraUtc;

        /// <summary>
        /// Reserves one login attempt BEFORE the password is checked, so attempts made at the same time cannot all be checked against the
        /// same count. Returns false when the user is locked: the sixth attempt after five reserved ones starts the lock. A correct password
        /// then clears the count (<see cref="ReiniciarFallosLogin"/>); a wrong one leaves the attempt counted.
        /// </summary>
        public bool ReservarIntentoLogin(DateTime ahoraUtc)
        {
            if (LoginBloqueado(ahoraUtc))
            {
                return false;
            }
            if (FallosLogin >= MaxFallosLogin)
            {
                LoginBloqueadoHastaUtc = ahoraUtc + BloqueoLogin;
                FallosLogin = 0;
                return false;
            }
            LoginBloqueadoHastaUtc = null;
            FallosLogin++;
            return true;
        }

        public void ReiniciarFallosLogin()
        {
            FallosLogin = 0;
            LoginBloqueadoHastaUtc = null;
        }

        public bool PinBloqueado(DateTime ahoraUtc) => PinBloqueadoHastaUtc > ahoraUtc;

        /// <summary>
        /// Reserves one PIN attempt BEFORE the PIN is checked (see <see cref="ReservarIntentoLogin"/>): at most five attempts can be pending
        /// in a window of ten minutes. A correct PIN gives its attempt back (<see cref="ConfirmarPinCorrecto"/>), so loading recepciones does
        /// not wear the allowance down, and a correct PIN never gives back the attempts that were wrong.
        /// </summary>
        public bool ReservarIntentoPin(DateTime ahoraUtc)
        {
            if (PinBloqueado(ahoraUtc))
            {
                return false;
            }
            if (PrimerFalloPinUtc is null || ahoraUtc - PrimerFalloPinUtc > VentanaPin)
            {
                FallosPin = 0;
                PrimerFalloPinUtc = ahoraUtc;
            }
            if (FallosPin >= MaxFallosPin)
            {
                PinBloqueadoHastaUtc = ahoraUtc + BloqueoPin;
                FallosPin = 0;
                PrimerFalloPinUtc = null;
                return false;
            }
            PinBloqueadoHastaUtc = null;
            FallosPin++;
            return true;
        }

        /// <summary>The PIN was right: its reserved attempt is given back.</summary>
        public void ConfirmarPinCorrecto()
        {
            if (FallosPin > 0)
            {
                FallosPin--;
            }
            if (FallosPin == 0)
            {
                PrimerFalloPinUtc = null;
            }
        }

        /// <summary>The PIN was wrong: when it was the fifth in the window the block starts now. Returns true if the PIN checks are now blocked.</summary>
        public bool ConfirmarPinIncorrecto(DateTime ahoraUtc)
        {
            if (FallosPin < MaxFallosPin)
            {
                return false;
            }
            PinBloqueadoHastaUtc = ahoraUtc + BloqueoPin;
            FallosPin = 0;
            PrimerFalloPinUtc = null;
            return true;
        }

        private static string ValidarUsername(string? username)
        {
            var trim = username?.Trim() ?? "";
            if (trim.Length == 0)
            {
                throw new ReglaDeNegocioException("El usuario no puede estar vacío.", nameof(username));
            }
            if (trim.Length < UsernameMinLength || trim.Length > UsernameMaxLength)
            {
                throw new ReglaDeNegocioException($"El usuario debe tener entre {UsernameMinLength} y {UsernameMaxLength} caracteres.", nameof(username));
            }
            if (!trim.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            {
                throw new ReglaDeNegocioException("El usuario solo puede tener letras sin acentos, números, punto, guion y guion bajo.", nameof(username));
            }
            return trim;
        }

        private static string ValidarNombre(string? nombre)
        {
            var trim = nombre?.Trim() ?? "";
            if (trim.Length == 0)
            {
                throw new ReglaDeNegocioException("El nombre no puede estar vacío.", nameof(nombre));
            }
            if (trim.Length > NombreMaxLength)
            {
                throw new ReglaDeNegocioException($"El nombre no puede superar los {NombreMaxLength} caracteres.", nameof(nombre));
            }
            return trim;
        }

        private static void ValidarRol(RolUsuario rol)
        {
            if (!Enum.IsDefined(rol))
            {
                throw new ReglaDeNegocioException("El rol no es válido.", nameof(rol));
            }
        }

        private static void ValidarHash(string? hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                throw new ReglaDeNegocioException("La contraseña es obligatoria.", "password");
            }
        }
    }
}
