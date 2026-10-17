using DairyControl.Application;
using DairyControl.Domain.Entities;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Infrastructure.Persistence
{
    // Builds the deterministic demo data in memory (no database), so it can be tested.
    public class DemoDataGenerator
    {
        public const int Semilla = 2026;
        public const int DiasHistorial = 182;
        public const int TicketInicial = 4900;

        // Added water: most deliveries have none; a few have a little; very few have a lot
        private const double ProbabilidadAguaAlta = 0.005; // 3.5% to 6%
        private const double ProbabilidadAguaBaja = 0.02; // 0.5% to 3%

        // Ranges taken from aggregated real reception logs
        private record PerfilTambo(string Nombre, int LitrosMin, int LitrosMax, int CadaDias, int HoraRetiro, bool RetiroTarde);

        private static readonly PerfilTambo[] Perfiles =
        [
            new("Tambo La Esperanza", 1400, 5000, 1, 6, true),
            new("Tambo El Ombú", 1400, 5000, 1, 6, true),
            new("Tambo San José", 800, 3500, 1, 6, false),
            new("Tambo Los Álamos", 800, 3500, 1, 7, false),
            new("Tambo La Aurora", 800, 3500, 1, 7, false),
            new("Tambo Don Ernesto", 300, 1300, 2, 11, false),
            new("Tambo El Trébol", 300, 1300, 2, 11, false),
            new("Tambo Las Acacias", 300, 1300, 2, 11, false)
        ];

        public record DatosDemo(IReadOnlyList<Proveedor> Proveedores, IReadOnlyList<RecepcionLeche> Recepciones);

        private record RecepcionPendiente(Proveedor Proveedor, ParametrosCalidad Parametros, DateTime FechaHora, int Silo, string? Observaciones);

        private readonly Random _random = new(Semilla);

        public DatosDemo Generar(DateTime desde, Guid creadoPorUsuarioId)
        {
            var proveedores = new List<Proveedor>();
            var recepciones = new List<RecepcionPendiente>();

            foreach (var perfil in Perfiles)
            {
                var proveedor = Proveedor.Crear(perfil.Nombre, creadoPorUsuarioId);

                for (var i = 0; i < DiasHistorial; i += perfil.CadaDias)
                {
                    var dia = desde.AddDays(i);
                    recepciones.Add(GenerarRecepcion(proveedor, perfil, dia.AddHours(perfil.HoraRetiro).AddMinutes(_random.Next(0, 180))));

                    if (perfil.RetiroTarde && _random.NextDouble() < 0.7)
                    {
                        recepciones.Add(GenerarRecepcion(proveedor, perfil, dia.AddHours(18).AddMinutes(_random.Next(0, 120))));
                    }
                }

                proveedores.Add(proveedor);
            }

            // One ticket sequence for the whole plant, in delivery order (OrderBy is stable, so ties stay deterministic).
            var ticket = TicketInicial;
            var ahora = desde.AddDays(DiasHistorial); // after the last generated reception
            var registradas = new List<RecepcionLeche>();
            foreach (var r in recepciones.OrderBy(r => r.FechaHora))
            {
                registradas.Add(RecepcionLeche.Registrar(r.Proveedor.Id, r.Parametros, r.FechaHora, ahora, r.Silo, r.Observaciones,
                    creadoPorUsuarioId, null, HoraPlanta.AUtc(r.FechaHora), ticket++));
            }

            return new DatosDemo(proveedores, registradas);
        }

        private RecepcionPendiente GenerarRecepcion(Proveedor proveedor, PerfilTambo perfil, DateTime fechaHora)
        {
            var litros = (decimal)_random.Next(perfil.LitrosMin, perfil.LitrosMax + 1);

            // ~70% of deliveries arrive cooled, the rest warm (no rejection threshold)
            var temperatura = _random.NextDouble() < 0.7
                ? EntreDecimales(3, 12, 1)
                : EntreDecimales(12, 25, 1);

            decimal? grasa = _random.NextDouble() < 0.33 ? EntreDecimales(3.2, 3.8, 2) : null;

            var acidezAlta = _random.NextDouble() < 0.05;
            var acidez = acidezAlta ? _random.Next(20, 24) : _random.Next(15, 20);
            var observaciones = acidezAlta ? "Acidez alta" : null;

            var sorteoAgua = _random.NextDouble();
            var agua = sorteoAgua < ProbabilidadAguaAlta ? EntreDecimales(3.5, 6, 1)
                : sorteoAgua < ProbabilidadAguaAlta + ProbabilidadAguaBaja ? EntreDecimales(0.5, 3, 1)
                : 0m;

            var parametros = ParametrosCalidad.Create(grasa, acidez, temperatura, litros, agua);
            return new RecepcionPendiente(proveedor, parametros, fechaHora, _random.Next(1, 4), observaciones);
        }

        private decimal EntreDecimales(double min, double max, int decimales)
        {
            return Math.Round((decimal)(min + _random.NextDouble() * (max - min)), decimales);
        }
    }
}
