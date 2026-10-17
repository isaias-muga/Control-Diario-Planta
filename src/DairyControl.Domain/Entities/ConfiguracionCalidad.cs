using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Domain.Entities
{
    // Single-row configuration of the plant, edited by an Admin: the quality limits currently in force and the number of silos.
    // (The name says "Calidad" because the limits came first; both are read by the same registration form.)
    public class ConfiguracionCalidad
    {
        public const int SilosMinimo = 1;
        public const int SilosMaximo = RecepcionLeche.SiloMax; // a plausible silo number is also the most silos a plant can have
        public const int SilosPredeterminados = 3;

        public static readonly Guid IdUnico = new("5b0c6f0e-3f6a-4f7e-9a53-0d1c2b7a4e11");

        public Guid Id { get; private set; }
        public LimitesCalidad Limites { get; private set; } = null!;

        /// <summary>How many silos the plant has: a new recepción must name one of 1..CantidadSilos.</summary>
        public int CantidadSilos { get; private set; }
        public DateTime ActualizadoEn { get; private set; }

        private ConfiguracionCalidad() { }

        public static ConfiguracionCalidad Crear(LimitesCalidad limites, DateTime actualizadoEnUtc, int cantidadSilos = SilosPredeterminados)
        {
            ArgumentNullException.ThrowIfNull(limites);
            ValidarCantidadSilos(cantidadSilos);
            return new ConfiguracionCalidad
            {
                Id = IdUnico,
                Limites = limites,
                CantidadSilos = cantidadSilos,
                ActualizadoEn = actualizadoEnUtc
            };
        }

        /// <summary>Changes the quality limits only; the number of silos stays.</summary>
        public void CambiarLimites(LimitesCalidad limites, DateTime actualizadoEnUtc) =>
            Actualizar(limites, CantidadSilos, actualizadoEnUtc);

        public void Actualizar(LimitesCalidad limites, int cantidadSilos, DateTime actualizadoEnUtc)
        {
            ArgumentNullException.ThrowIfNull(limites);
            ValidarCantidadSilos(cantidadSilos);
            Limites = limites;
            CantidadSilos = cantidadSilos;
            ActualizadoEn = actualizadoEnUtc;
        }

        /// <summary>A silo is required and must exist: between 1 and the number of silos of the plant. History is not touched when the number goes down.</summary>
        public void ValidarSilo(int? silo)
        {
            if (silo is null)
            {
                throw new ReglaDeNegocioException("El silo es obligatorio.", nameof(silo));
            }
            if (silo < SilosMinimo || silo > CantidadSilos)
            {
                throw new ReglaDeNegocioException($"El silo debe estar entre {SilosMinimo} y {CantidadSilos}.", nameof(silo));
            }
        }

        private static void ValidarCantidadSilos(int cantidadSilos)
        {
            if (cantidadSilos < SilosMinimo || cantidadSilos > SilosMaximo)
            {
                throw new ReglaDeNegocioException($"La cantidad de silos debe estar entre {SilosMinimo} y {SilosMaximo}.", nameof(cantidadSilos));
            }
        }
    }
}
