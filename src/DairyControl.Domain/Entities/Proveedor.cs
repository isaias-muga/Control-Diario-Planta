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

        public static Proveedor Crear(string nombre, Guid creadoPorUsuarioId)
        {
            if (creadoPorUsuarioId == Guid.Empty)
            {
                throw new ReglaDeNegocioException("El usuario que crea el proveedor es obligatorio.", nameof(creadoPorUsuarioId));
            }

            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new ReglaDeNegocioException("El nombre del proveedor no puede estar vacío.", nameof(nombre));
            }

            var nombreTrim = nombre.Trim();

            if (nombreTrim.Length > NombreMaxLength)
            {
                throw new ReglaDeNegocioException($"El nombre del proveedor no puede exceder {NombreMaxLength} caracteres.", nameof(nombre));
            }
            return new Proveedor()
            {
                Id = Guid.NewGuid(),
                Nombre = nombreTrim,
                CreadoPorUsuarioId = creadoPorUsuarioId
            };


        }

        private Proveedor() { }
    }
}
