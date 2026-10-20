using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;

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

        /// <summary>The history entry of the creation of this operario.</summary>
        public CambioRegistrado RegistrarAlta(Guid usuarioId, DateTime enUtc) =>
            CambioRegistrado.Crear(CambioRegistrado.EntidadOperario, Id, Guid.NewGuid(), CambioRegistrado.Campos.Alta, null, Nombre, usuarioId, enUtc, MotivoDeCambio.Create("Alta de operario"));

        public CambioRegistrado Desactivar(MotivoDeCambio motivo, Guid usuarioId, DateTime enUtc)
        {
            ArgumentNullException.ThrowIfNull(motivo);
            if (!Activo)
            {
                throw new ReglaDeNegocioException("El operario ya está desactivado.", "id");
            }
            var cambio = CambioRegistrado.Crear(CambioRegistrado.EntidadOperario, Id, Guid.NewGuid(), CambioRegistrado.Campos.Activo, "Activo", "Inactivo", usuarioId, enUtc, motivo);
            Activo = false;
            return cambio;
        }

        /// <summary>Its PIN works again. The caller checks that no other active operario uses the same PIN.</summary>
        public CambioRegistrado Reactivar(MotivoDeCambio motivo, Guid usuarioId, DateTime enUtc)
        {
            ArgumentNullException.ThrowIfNull(motivo);
            if (Activo)
            {
                throw new ReglaDeNegocioException("El operario ya está activo.", "id");
            }
            var cambio = CambioRegistrado.Crear(CambioRegistrado.EntidadOperario, Id, Guid.NewGuid(), CambioRegistrado.Campos.Activo, "Inactivo", "Activo", usuarioId, enUtc, motivo);
            Activo = true;
            return cambio;
        }

        /// <summary>The history says that the PIN changed, never the PIN or its hash.</summary>
        public CambioRegistrado CambiarPin(string pinHash, Guid usuarioId, DateTime enUtc)
        {
            ValidarHash(pinHash);
            var cambio = CambioRegistrado.Crear(CambioRegistrado.EntidadOperario, Id, Guid.NewGuid(), CambioRegistrado.Campos.Pin, null, "Cambiado", usuarioId, enUtc, MotivoDeCambio.Create("PIN cambiado"));
            PinHash = pinHash;
            return cambio;
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
