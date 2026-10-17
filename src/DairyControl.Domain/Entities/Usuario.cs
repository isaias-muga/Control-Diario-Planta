using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;

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

        public void Desactivar()
        {
            Activo = false;
            SecurityStamp = Guid.NewGuid();
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
        public void RestablecerPassword(string passwordHash)
        {
            ValidarHash(passwordHash);
            PasswordHash = passwordHash;
            DebeCambiarPassword = true;
            SecurityStamp = Guid.NewGuid();
            ReiniciarFallosLogin();
        }

        public void CambiarRol(RolUsuario rol)
        {
            ValidarRol(rol);
            Rol = rol;
            SecurityStamp = Guid.NewGuid();
        }

        public bool LoginBloqueado(DateTime ahoraUtc) => LoginBloqueadoHastaUtc > ahoraUtc;

        public void RegistrarFalloLogin(DateTime ahoraUtc)
        {
            if (LoginBloqueado(ahoraUtc))
            {
                return;
            }
            FallosLogin++;
            if (FallosLogin >= MaxFallosLogin)
            {
                LoginBloqueadoHastaUtc = ahoraUtc + BloqueoLogin;
                FallosLogin = 0;
            }
        }

        public void ReiniciarFallosLogin()
        {
            FallosLogin = 0;
            LoginBloqueadoHastaUtc = null;
        }

        public bool PinBloqueado(DateTime ahoraUtc) => PinBloqueadoHastaUtc > ahoraUtc;

        /// <summary>Counts a wrong PIN; the fifth one inside the window blocks PIN checks for a while.</summary>
        public void RegistrarPinIncorrecto(DateTime ahoraUtc)
        {
            if (PrimerFalloPinUtc is null || ahoraUtc - PrimerFalloPinUtc > VentanaPin)
            {
                FallosPin = 0;
                PrimerFalloPinUtc = ahoraUtc;
            }
            FallosPin++;
            if (FallosPin >= MaxFallosPin)
            {
                PinBloqueadoHastaUtc = ahoraUtc + BloqueoPin;
                FallosPin = 0;
                PrimerFalloPinUtc = null;
            }
        }

        public void RegistrarPinCorrecto()
        {
            FallosPin = 0;
            PrimerFalloPinUtc = null;
            PinBloqueadoHastaUtc = null;
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
