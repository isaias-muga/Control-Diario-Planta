using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Domain.Entities
{

    public class Proveedor
    {
        public const int NombreMaxLength = 200;
        public Guid Id { get; private set; }
        public string Nombre { get; private set; } = null!;

        /// <summary>Who created it (another aggregate, by id). Null for rows that existed before users did.</summary>
        public Guid? CreadoPorUsuarioId { get; private set; }

        /// <summary>
        /// An inactive proveedor cannot be chosen for a new recepción but keeps all its history. Proveedores are never deleted.
        /// </summary>
        public bool Activo { get; private set; } = true;

        public static Proveedor Crear(string nombre, Guid creadoPorUsuarioId)
        {
            if (creadoPorUsuarioId == Guid.Empty)
            {
                throw new ReglaDeNegocioException("El usuario que crea el proveedor es obligatorio.", nameof(creadoPorUsuarioId));
            }

            return new Proveedor()
            {
                Id = Guid.NewGuid(),
                Nombre = NormalizarNombre(nombre),
                CreadoPorUsuarioId = creadoPorUsuarioId
            };
        }

        /// <summary>Corrects the name (with a reason) and returns the history entry. The old name stays in the history.</summary>
        public CambioRegistrado Renombrar(string nombre, MotivoDeCambio motivo, Guid usuarioId, DateTime enUtc)
        {
            ArgumentNullException.ThrowIfNull(motivo);
            var nuevo = NormalizarNombre(nombre);
            if (nuevo == Nombre)
            {
                throw new ReglaDeNegocioException("El nombre no cambió.", nameof(nombre));
            }
            var cambio = CambioRegistrado.Crear(CambioRegistrado.EntidadProveedor, Id, Guid.NewGuid(), CambioRegistrado.Campos.Nombre, Nombre, nuevo, usuarioId, enUtc, motivo);
            Nombre = nuevo;
            return cambio;
        }

        public CambioRegistrado Desactivar(MotivoDeCambio motivo, Guid usuarioId, DateTime enUtc)
        {
            ArgumentNullException.ThrowIfNull(motivo);
            if (!Activo)
            {
                throw new ReglaDeNegocioException("El proveedor ya está desactivado.", "id");
            }
            var cambio = CambioRegistrado.Crear(CambioRegistrado.EntidadProveedor, Id, Guid.NewGuid(), CambioRegistrado.Campos.Activo, "Activo", "Inactivo", usuarioId, enUtc, motivo);
            Activo = false;
            return cambio;
        }

        public CambioRegistrado Reactivar(MotivoDeCambio motivo, Guid usuarioId, DateTime enUtc)
        {
            ArgumentNullException.ThrowIfNull(motivo);
            if (Activo)
            {
                throw new ReglaDeNegocioException("El proveedor ya está activo.", "id");
            }
            var cambio = CambioRegistrado.Crear(CambioRegistrado.EntidadProveedor, Id, Guid.NewGuid(), CambioRegistrado.Campos.Activo, "Inactivo", "Activo", usuarioId, enUtc, motivo);
            Activo = true;
            return cambio;
        }

        private static string NormalizarNombre(string? nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new ReglaDeNegocioException("El nombre del proveedor no puede estar vacío.", nameof(nombre));
            }

            var nombreTrim = nombre.Trim();

            if (nombreTrim.Length > NombreMaxLength)
            {
                throw new ReglaDeNegocioException($"El nombre del proveedor no puede exceder {NombreMaxLength} caracteres.", nameof(nombre));
            }
            return nombreTrim;
        }

        private Proveedor() { }
    }
}
