using DairyControl.Application;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DairyControl.Infrastructure.Persistence
{
    public class DemoDataSeeder
    {

        private readonly AppDbContext _context;
        private readonly TimeProvider _reloj;
        private readonly ILogger<DemoDataSeeder>? _logger;

        public DemoDataSeeder(AppDbContext context, TimeProvider reloj, ILogger<DemoDataSeeder>? logger = null)
        {
            _context = context;
            _reloj = reloj;
            _logger = logger;
        }

        // Startup entry point. Reset wins over seed: it wipes everything and reseeds, even if
        // SeedOnStartup is false. Otherwise the seed only runs on an empty database.
        public async Task InitializeAsync(bool resetOnStartup, bool seedOnStartup)
        {
            if (resetOnStartup)
            {
                await ResetAndSeedAsync();
            }
            else if (seedOnStartup)
            {
                await SeedAsync();
            }
        }

        public async Task SeedAsync()
        {
            if (await _context.Proveedores.AnyAsync())
                return;

            await AddDemoDataAsync();
            await _context.SaveChangesAsync();
        }

        // Deletes every recepción and proveedor, restores the default quality limits and reseeds,
        // all in one transaction. Users and operarios are never touched.
        public async Task ResetAndSeedAsync()
        {
            var proveedoresBorrados = 0;
            var recepcionesBorradas = 0;

            // EnableRetryOnFailure: explicit transactions must run inside the execution strategy
            var strategy = _context.Database.CreateExecutionStrategy();
            try
            {
            await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear(); // a retried attempt starts from a clean state
                await using var transaccion = await _context.Database.BeginTransactionAsync();

                // The demo reset is the one place that deletes recepciones and proveedores (real data is only ever corrected, voided or
                // deactivated); the history that describes them goes with them. The history of usuarios and operarios stays: they are
                // never deleted. An architecture test allows only this file.
                await _context.HistorialCambios
                    .Where(c => c.Entidad == CambioRegistrado.EntidadRecepcion || c.Entidad == CambioRegistrado.EntidadProveedor)
                    .ExecuteDeleteAsync();
                recepcionesBorradas = await _context.Recepciones.ExecuteDeleteAsync();
                proveedoresBorrados = await _context.Proveedores.ExecuteDeleteAsync();

                var configuracion = await _context.ConfiguracionesCalidad.FirstOrDefaultAsync();
                if (configuracion is null)
                {
                    _context.ConfiguracionesCalidad.Add(ConfiguracionCalidad.Crear(LimitesCalidad.Predeterminados, _reloj.GetUtcNow().UtcDateTime));
                }
                else
                {
                    configuracion.Actualizar(LimitesCalidad.Predeterminados, ConfiguracionCalidad.SilosPredeterminados, _reloj.GetUtcNow().UtcDateTime);
                }

                await AddDemoDataAsync();
                await _context.SaveChangesAsync();
                await transaccion.CommitAsync();
            });
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another instance started at the same time and changed the quality limits first (the row has a version):
                // its reset is the one that counts, and this transaction was rolled back.
                _logger?.LogWarning("Demo data reset skipped: another instance changed the quality limits while this one was resetting.");
                return;
            }

            _logger?.LogWarning(
                "Demo data reset: deleted {Proveedores} proveedores and {Recepciones} recepciones, restored the default quality limits and reseeded.",
                proveedoresBorrados,
                recepcionesBorradas);
        }

        private async Task AddDemoDataAsync()
        {
            // The demo rows are created by the first admin (the users themselves are never reset)
            var adminId = await _context.Usuarios.AsNoTracking()
                .Where(u => u.Rol == RolUsuario.Admin)
                .OrderBy(u => u.Username)
                .Select(u => u.Id)
                .FirstOrDefaultAsync();
            if (adminId == Guid.Empty)
            {
                throw new InvalidOperationException("No hay un administrador que figure como creador de los datos de demostración.");
            }

            var ahoraPlanta = HoraPlanta.Ahora(_reloj);
            var desde = ahoraPlanta.Date.AddDays(-DemoDataGenerator.DiasHistorial);

            var datos = new DemoDataGenerator().Generar(desde, adminId);
            _context.Proveedores.AddRange(datos.Proveedores);
            _context.Recepciones.AddRange(datos.Recepciones);
        }
    }
}
