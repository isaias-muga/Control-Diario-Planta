using DairyControl.Domain.Exceptions;

namespace DairyControl.Domain.Entities
{
    /// <summary>A person at the plant who loads recepciones from the shared reception phone and identifies with a 4-digit PIN.
    /// Only the keyed hash of the PIN is stored (computed outside the domain).</summary>
    public class Operario
    {
        public const int NombreMaxLength = 100;
        public const int PinLength = 4;

        public Guid Id { get; private set; }
        public string Nombre { get; private set; } = null!;
        public bool Activo { get; private set; }
        public string PinHash { get; private set; } = null!;

        private Operario() { }

        public static Operario Crear(string nombre, string pinHash)
        {
            var nombreTrim = nombre?.Trim() ?? "";
            if (nombreTrim.Length == 0)
            {
                throw new ReglaDeNegocioException("El nombre del operario no puede estar vacío.", nameof(nombre));
            }
            if (nombreTrim.Length > NombreMaxLength)
            {
                throw new ReglaDeNegocioException($"El nombre del operario no puede superar los {NombreMaxLength} caracteres.", nameof(nombre));
            }
            ValidarHash(pinHash);

            return new Operario { Id = Guid.NewGuid(), Nombre = nombreTrim, Activo = true, PinHash = pinHash };
        }

        /// <summary>The rule for a PIN (exactly four digits), checked before it is hashed.</summary>
        public static void ValidarPin(string? pin, string campo = "pin")
        {
            if (pin is null || pin.Length != PinLength || !pin.All(char.IsAsciiDigit))
            {
                throw new ReglaDeNegocioException($"El PIN debe tener {PinLength} dígitos.", campo);
            }
        }

        public void Desactivar() => Activo = false;

        public void CambiarPin(string pinHash)
        {
            ValidarHash(pinHash);
            PinHash = pinHash;
        }

        private static void ValidarHash(string? hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                throw new ReglaDeNegocioException("El PIN es obligatorio.", "pin");
            }
        }
    }
}
