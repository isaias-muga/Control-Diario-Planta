using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Domain.Entities
{
    /// <summary>
    /// One field that changed in a correction (or the void of a recepción, or the deactivation of a proveedor): what it was, what it is,
    /// who did it, when and why. The history is append-only: nothing in the system updates or deletes these rows, and a change to a
    /// recepción or a proveedor is saved together with its entries. It is its own aggregate root (the history grows without bound, so it
    /// never lives inside the recepción) and it refers to what it describes by id.
    /// </summary>
    public class CambioRegistrado
    {
        public const string EntidadRecepcion = "Recepcion";
        public const string EntidadProveedor = "Proveedor";
        public const int CampoMaxLength = 50;
        public const int ValorMaxLength = 500;

        /// <summary>The fields a history entry can name.</summary>
        public static class Campos
        {
            public const string Proveedor = "proveedor";
            public const string Litros = "litros";
            public const string Grasa = "grasa";
            public const string Acidez = "acidez";
            public const string Temperatura = "temperatura";
            public const string Agua = "agua";
            public const string Silo = "silo";
            public const string Ticket = "ticket";
            public const string FechaHora = "fechaHora";
            public const string Observaciones = "observaciones";
            public const string Anulacion = "anulacion";
            public const string Nombre = "nombre";
            public const string Activo = "activo";
        }

        public Guid Id { get; private set; }
        public string Entidad { get; private set; } = null!;
        public Guid EntidadId { get; private set; }

        /// <summary>Groups the entries of one correction (a correction of three fields writes three entries with the same id).</summary>
        public Guid CorreccionId { get; private set; }
        public string Campo { get; private set; } = null!;
        public string? ValorAnterior { get; private set; }
        public string? ValorNuevo { get; private set; }
        public Guid UsuarioId { get; private set; }

        /// <summary>UTC, from the TimeProvider.</summary>
        public DateTime EnUtc { get; private set; }
        public string Motivo { get; private set; } = null!;

        private CambioRegistrado() { }

        public static CambioRegistrado Crear(string entidad, Guid entidadId, Guid correccionId, string campo, string? valorAnterior, string? valorNuevo,
            Guid usuarioId, DateTime enUtc, MotivoDeCambio motivo)
        {
            ArgumentNullException.ThrowIfNull(motivo);
            if (entidad != EntidadRecepcion && entidad != EntidadProveedor)
            {
                throw new ReglaDeNegocioException("La entidad del cambio no es válida.", nameof(entidad));
            }
            if (entidadId == Guid.Empty || correccionId == Guid.Empty)
            {
                throw new ReglaDeNegocioException("El cambio debe indicar qué registro modifica.", nameof(entidadId));
            }
            if (usuarioId == Guid.Empty)
            {
                throw new ReglaDeNegocioException("El usuario que hace el cambio es obligatorio.", nameof(usuarioId));
            }
            if (string.IsNullOrWhiteSpace(campo) || campo.Length > CampoMaxLength)
            {
                throw new ReglaDeNegocioException("El campo del cambio no es válido.", nameof(campo));
            }
            if (enUtc == default)
            {
                throw new ReglaDeNegocioException("La fecha del cambio es obligatoria.", nameof(enUtc));
            }
            return new CambioRegistrado
            {
                Id = Guid.NewGuid(),
                Entidad = entidad,
                EntidadId = entidadId,
                CorreccionId = correccionId,
                Campo = campo,
                ValorAnterior = Recortar(valorAnterior),
                ValorNuevo = Recortar(valorNuevo),
                UsuarioId = usuarioId,
                EnUtc = enUtc,
                Motivo = motivo.Texto
            };
        }

        private static string? Recortar(string? valor) =>
            valor is { Length: > ValorMaxLength } ? valor[..ValorMaxLength] : valor;
    }
}
