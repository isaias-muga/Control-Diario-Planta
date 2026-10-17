using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Rules;
using DairyControl.Domain.ValueObjects;
namespace DairyControl.Domain.Entities
{
    public class RecepcionLeche
    {
        public const int ObservacionesMaxLength = 500;
        public const int SiloMin = 1; // plausible silo numbers (a typing-error check, not a quality limit)
        public const int SiloMax = 20;
        public static readonly DateTime FechaMinima = new(2000, 1, 1); // plausible dates (a typing-error check): from 2000-01-01 to now + 1 hour, in plant time
        public const int AdelantoMaximoHoras = 1;
        public ParametrosCalidad Parametros { get; private set; } = null!;
        public Guid Id { get; private set; }
        public Guid ProveedorId { get; private set; } // another aggregate is referenced by id
        public DateTime FechaHora { get; private set; }
        public int? Silo { get; private set; }
        public string? Observaciones { get; private set; }
        public int? Ticket { get; private set; }

        // Audit: the server stamps who registered it, from the token and the PIN, never from the request. Null for rows that
        // existed before users did. RegistradoEn is a UTC timestamp; FechaHora stays in plant time.
        public Guid? RegistradoPorUsuarioId { get; private set; }
        public Guid? OperarioId { get; private set; }
        public DateTime? RegistradoEn { get; private set; }

        // Liters to pay: the added water is deducted (water null = 0). The formula is defined once (LitrosAPagarRegla);
        // the queries use the same expression in SQL.
        public decimal LitrosAPagar => Math.Round(LitrosAPagarRegla.De(this), ParametrosCalidad.ScaleLitros);

        private RecepcionLeche() { }

        public static RecepcionLeche Registrar(Guid proveedorId, ParametrosCalidad parametros, DateTime fechaHora, DateTime ahoraPlanta, int? silo, string? observaciones,
            Guid registradoPorUsuarioId, Guid? operarioId, DateTime registradoEnUtc, int? ticket = null)
        {
            ArgumentNullException.ThrowIfNull(parametros);
            if (proveedorId == Guid.Empty)
            {
                throw new ReglaDeNegocioException("El proveedor es obligatorio.", nameof(proveedorId));
            }
            if (registradoPorUsuarioId == Guid.Empty)
            {
                throw new ReglaDeNegocioException("El usuario que registra la recepción es obligatorio.", nameof(registradoPorUsuarioId));
            }
            if (fechaHora == default)
            {
                throw new ReglaDeNegocioException("La fecha y hora es obligatoria.", nameof(fechaHora));
            }
            if (fechaHora < FechaMinima)
            {
                throw new ReglaDeNegocioException($"La fecha y hora no puede ser anterior al {FechaMinima:dd/MM/yyyy}.", nameof(fechaHora));
            }
            if (fechaHora > ahoraPlanta.AddHours(AdelantoMaximoHoras))
            {
                throw new ReglaDeNegocioException($"La fecha y hora no puede superar la hora actual en más de {AdelantoMaximoHoras} hora.", nameof(fechaHora));
            }
            if (ticket != null && ticket < 1)
            {
                throw new ReglaDeNegocioException("El número de ticket debe ser un entero positivo.", nameof(ticket));
            }
            if (silo != null && (silo < SiloMin || silo > SiloMax))
            {
                throw new ReglaDeNegocioException($"El silo debe estar entre {SiloMin} y {SiloMax}.", nameof(silo));
            }
            var observacionesTrim = observaciones?.Trim();
            if (observacionesTrim?.Length == 0)
            {
                observacionesTrim = null; // blank text counts as no observaciones
            }
            if (observacionesTrim != null && observacionesTrim.Length > ObservacionesMaxLength)
            {
                throw new ReglaDeNegocioException($"Las observaciones no pueden superar los {ObservacionesMaxLength} caracteres.", nameof(observaciones));
            }
            return new RecepcionLeche
            {
                Id = Guid.NewGuid(),
                ProveedorId = proveedorId,
                FechaHora = fechaHora,
                Silo = silo,
                Observaciones = observacionesTrim,
                Ticket = ticket,
                Parametros = parametros,
                RegistradoPorUsuarioId = registradoPorUsuarioId,
                OperarioId = operarioId,
                RegistradoEn = registradoEnUtc
            };
        }

    }
}
