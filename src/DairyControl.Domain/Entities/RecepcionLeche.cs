using System.Globalization;
using System.Linq.Expressions;
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

        /// <summary>
        /// The recepciones that count: a voided one stays in the table (and keeps its ticket used) but is left out of every list, total and
        /// Resumen figure. Defined once, as an expression so EF Core can translate it: the queries use this, nobody copies the condition.
        /// </summary>
        public static readonly Expression<Func<RecepcionLeche, bool>> Vigente = r => r.AnuladaEn == null;

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

        // Void ("anular"): final, with a reason. Null while the recepción is valid. Who and when are UTC audit data.
        public DateTime? AnuladaEn { get; private set; }
        public Guid? AnuladaPorUsuarioId { get; private set; }
        public string? MotivoAnulacion { get; private set; }

        public bool EstaAnulada => AnuladaEn != null;

        // Liters to pay: the added water is deducted (water null = 0). The formula is defined once (LitrosAPagarRegla);
        // the queries use the same expression in SQL.
        public decimal LitrosAPagar => Math.Round(LitrosAPagarRegla.De(this), ParametrosCalidad.ScaleLitros);

        private RecepcionLeche() { }

        public static RecepcionLeche Registrar(Guid proveedorId, ParametrosCalidad parametros, DateTime fechaHora, DateTime ahoraPlanta, int? silo, string? observaciones,
            Guid registradoPorUsuarioId, Guid? operarioId, DateTime registradoEnUtc, int? ticket = null)
        {
            ArgumentNullException.ThrowIfNull(parametros);
            ValidarProveedor(proveedorId);
            if (registradoPorUsuarioId == Guid.Empty)
            {
                throw new ReglaDeNegocioException("El usuario que registra la recepción es obligatorio.", nameof(registradoPorUsuarioId));
            }
            ValidarFechaHora(fechaHora, ahoraPlanta);
            ValidarTicket(ticket);
            ValidarSilo(silo);
            var observacionesTrim = NormalizarObservaciones(observaciones);
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

        /// <summary>
        /// Corrects the recepción with the same checks as <see cref="Registrar"/> and returns one history entry per field that changed
        /// (all with the same correction id). Nothing changes, and it throws, when no field differs. A voided recepción cannot be
        /// corrected, and an existing ticket cannot be removed (that would free a number that was used).
        /// The silo against the configured number of silos and the uniqueness of the ticket are checked by the caller, as when registering.
        /// </summary>
        public IReadOnlyList<CambioRegistrado> Corregir(Guid proveedorId, string proveedorNombreAnterior, string proveedorNombreNuevo, ParametrosCalidad parametros,
            DateTime fechaHora, DateTime ahoraPlanta, int? silo, string? observaciones, int? ticket, MotivoDeCambio motivo, Guid usuarioId, DateTime enUtc)
        {
            ArgumentNullException.ThrowIfNull(parametros);
            ArgumentNullException.ThrowIfNull(motivo);
            if (EstaAnulada)
            {
                throw new ReglaDeNegocioException("La recepción está anulada y no se puede corregir.", "id");
            }
            ValidarProveedor(proveedorId);
            if (usuarioId == Guid.Empty)
            {
                throw new ReglaDeNegocioException("El usuario que corrige la recepción es obligatorio.", nameof(usuarioId));
            }
            ValidarFechaHora(fechaHora, ahoraPlanta);
            ValidarTicket(ticket);
            if (Ticket != null && ticket == null)
            {
                throw new ReglaDeNegocioException("El ticket no se puede quitar: queda usado.", nameof(ticket));
            }
            ValidarSilo(silo);
            var observacionesTrim = NormalizarObservaciones(observaciones);

            var correccionId = Guid.NewGuid();
            var cambios = new List<CambioRegistrado>();
            void Anotar(string campo, string? anterior, string? nuevo)
            {
                if (anterior != nuevo)
                {
                    cambios.Add(CambioRegistrado.Crear(CambioRegistrado.EntidadRecepcion, Id, correccionId, campo, anterior, nuevo, usuarioId, enUtc, motivo));
                }
            }

            Anotar(CambioRegistrado.Campos.Proveedor, ProveedorId == proveedorId ? null : proveedorNombreAnterior, ProveedorId == proveedorId ? null : proveedorNombreNuevo);
            Anotar(CambioRegistrado.Campos.Litros, Texto(Parametros.Litros, "0.###"), Texto(parametros.Litros, "0.###"));
            Anotar(CambioRegistrado.Campos.Grasa, Texto(Parametros.Grasa, "0.##"), Texto(parametros.Grasa, "0.##"));
            Anotar(CambioRegistrado.Campos.Acidez, Texto(Parametros.Acidez, "0.##"), Texto(parametros.Acidez, "0.##"));
            Anotar(CambioRegistrado.Campos.Temperatura, Texto(Parametros.Temperatura, "0.##"), Texto(parametros.Temperatura, "0.##"));
            Anotar(CambioRegistrado.Campos.Agua, Texto(Parametros.AguaPorcentaje, "0.##"), Texto(parametros.AguaPorcentaje, "0.##"));
            Anotar(CambioRegistrado.Campos.Silo, Texto(Silo), Texto(silo));
            Anotar(CambioRegistrado.Campos.Ticket, Texto(Ticket), Texto(ticket));
            Anotar(CambioRegistrado.Campos.FechaHora, Texto(FechaHora), Texto(fechaHora));
            Anotar(CambioRegistrado.Campos.Observaciones, Observaciones, observacionesTrim);

            if (cambios.Count == 0)
            {
                throw new ReglaDeNegocioException("No hay cambios para guardar.", "cambios");
            }

            ProveedorId = proveedorId;
            Parametros = parametros;
            FechaHora = fechaHora;
            Silo = silo;
            Ticket = ticket;
            Observaciones = observacionesTrim;
            return cambios;
        }

        /// <summary>Voids the recepción for good (there is no way back) and returns the history entry. It stays in the table and keeps its ticket used.</summary>
        public CambioRegistrado Anular(MotivoDeCambio motivo, Guid usuarioId, DateTime enUtc)
        {
            ArgumentNullException.ThrowIfNull(motivo);
            if (EstaAnulada)
            {
                throw new ReglaDeNegocioException("La recepción ya está anulada.", "id");
            }
            if (usuarioId == Guid.Empty)
            {
                throw new ReglaDeNegocioException("El usuario que anula la recepción es obligatorio.", nameof(usuarioId));
            }
            var cambio = CambioRegistrado.Crear(CambioRegistrado.EntidadRecepcion, Id, Guid.NewGuid(), CambioRegistrado.Campos.Anulacion, "Vigente", "Anulada", usuarioId, enUtc, motivo);
            AnuladaEn = enUtc;
            AnuladaPorUsuarioId = usuarioId;
            MotivoAnulacion = motivo.Texto;
            return cambio;
        }

        private static void ValidarProveedor(Guid proveedorId)
        {
            if (proveedorId == Guid.Empty)
            {
                throw new ReglaDeNegocioException("El proveedor es obligatorio.", nameof(proveedorId));
            }
        }

        private static void ValidarFechaHora(DateTime fechaHora, DateTime ahoraPlanta)
        {
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
        }

        private static void ValidarTicket(int? ticket)
        {
            if (ticket != null && ticket < 1)
            {
                throw new ReglaDeNegocioException("El número de ticket debe ser un entero positivo.", nameof(ticket));
            }
        }

        private static void ValidarSilo(int? silo)
        {
            if (silo is null)
            {
                throw new ReglaDeNegocioException("El silo es obligatorio.", nameof(silo));
            }
            if (silo < SiloMin || silo > SiloMax)
            {
                throw new ReglaDeNegocioException($"El silo debe estar entre {SiloMin} y {SiloMax}.", nameof(silo));
            }
        }

        private static string? NormalizarObservaciones(string? observaciones)
        {
            var observacionesTrim = observaciones?.Trim();
            if (observacionesTrim?.Length == 0)
            {
                observacionesTrim = null; // blank text counts as no observaciones
            }
            if (observacionesTrim != null && observacionesTrim.Length > ObservacionesMaxLength)
            {
                throw new ReglaDeNegocioException($"Las observaciones no pueden superar los {ObservacionesMaxLength} caracteres.", nameof(observaciones));
            }
            return observacionesTrim;
        }

        // Values of the history, always in the same invariant form so that two equal values compare equal
        private static string? Texto(decimal? valor, string formato) => valor?.ToString(formato, CultureInfo.InvariantCulture);
        private static string? Texto(int? valor) => valor?.ToString(CultureInfo.InvariantCulture);
        private static string Texto(DateTime valor) => valor.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
