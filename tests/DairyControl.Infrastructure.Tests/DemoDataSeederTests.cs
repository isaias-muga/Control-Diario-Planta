using DairyControl.Domain.Entities;
using DairyControl.Domain.ValueObjects;
using DairyControl.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DairyControl.Infrastructure.Tests
{
    // In-memory SQLite, schema from EnsureCreated (the migrations are SQL Server-specific).
    // These tests only cover the reset/seed logic: they do not prove SQL Server behavior
    // (migrations, unique-violation error codes, query translation).
    public class DemoDataSeederTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly Guid _adminId;

        public DemoDataSeederTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            using var context = NuevoContexto();
            context.Database.EnsureCreated();
            _adminId = UsuariosDePrueba.CrearAdmin(context);
        }

        public void Dispose() => _connection.Dispose();

        private AppDbContext NuevoContexto() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

        private sealed class LoggerEnMemoria : ILogger<DemoDataSeeder>
        {
            public List<(LogLevel Nivel, string Mensaje)> Entradas { get; } = [];

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                Entradas.Add((logLevel, formatter(state, exception)));
        }

        private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => ahora;
        }

        // Fails on save: the deletes have already run inside the transaction by then
        private sealed class ContextoQueFallaAlGuardar(DbContextOptions<AppDbContext> options) : AppDbContext(options)
        {
            public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("Fallo simulado al guardar.");
        }

        // Another instance changed the quality limits between this one's read and its save
        private sealed class ContextoConConflictoDeConcurrencia(DbContextOptions<AppDbContext> options) : AppDbContext(options)
        {
            public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
                throw new DbUpdateConcurrencyException("Otra instancia cambió los límites.");
        }

        // Existing data that a reset must wipe: an old proveedor with one recepción, and custom limits
        private void CargarDatosViejos()
        {
            using var context = NuevoContexto();
            var viejo = Proveedor.Crear("Tambo Viejo", _adminId);
            context.Proveedores.Add(viejo);
            context.Recepciones.Add(RecepcionLeche.Registrar(viejo.Id, ParametrosCalidad.Create(null, 17m, 5m, 1000m), new DateTime(2026, 1, 1, 6, 0, 0), new DateTime(2026, 1, 2), 1, null, _adminId, null, new DateTime(2026, 1, 2), 1));
            context.ConfiguracionesCalidad.Single().CambiarLimites(LimitesCalidad.Create(25m, 30m, 5m), DateTime.UtcNow);
            context.SaveChanges();
        }

        private (int Proveedores, int Recepciones, string[] Nombres, List<int> Tickets, LimitesCalidad Limites) Leer()
        {
            using var context = NuevoContexto();
            var proveedores = context.Proveedores.AsNoTracking().ToList();
            var recepciones = context.Recepciones.AsNoTracking().ToList();
            return (
                proveedores.Count,
                recepciones.Count,
                proveedores.Select(p => p.Nombre).ToArray(),
                recepciones.Select(r => r.Ticket!.Value).OrderBy(t => t).ToList(),
                context.ConfiguracionesCalidad.AsNoTracking().Single().Limites);
        }

        [Fact]
        public async Task InitializeAsync_ResetOn_LeavesOnlyTheSeededData()
        {
            // Arrange
            CargarDatosViejos();
            var logger = new LoggerEnMemoria();
            using var context = NuevoContexto();
            var seeder = new DemoDataSeeder(context, TimeProvider.System, logger);

            // Act
            await seeder.InitializeAsync(resetOnStartup: true, seedOnStartup: false);

            // Assert
            var datos = Leer();
            Assert.Equal(8, datos.Proveedores);
            Assert.DoesNotContain("Tambo Viejo", datos.Nombres);
            Assert.NotEmpty(datos.Tickets);
            Assert.Equal(Enumerable.Range(4900, datos.Recepciones), datos.Tickets); // unique and consecutive
            Assert.Equal(LimitesCalidad.Predeterminados, datos.Limites);
        }

        [Fact]
        public async Task InitializeAsync_ResetOn_KeepsTheUsersAndCreditsTheSeededRowsToTheAdmin()
        {
            // Arrange
            using var context = NuevoContexto();
            var seeder = new DemoDataSeeder(context, TimeProvider.System, new LoggerEnMemoria());

            // Act
            await seeder.InitializeAsync(resetOnStartup: true, seedOnStartup: false);

            // Assert
            using var lectura = NuevoContexto();
            Assert.Equal(_adminId, lectura.Usuarios.AsNoTracking().Single().Id);
            Assert.All(lectura.Proveedores.AsNoTracking().ToList(), p => Assert.Equal(_adminId, p.CreadoPorUsuarioId));
            Assert.All(lectura.Recepciones.AsNoTracking().ToList(), r =>
            {
                Assert.Equal(_adminId, r.RegistradoPorUsuarioId);
                Assert.Null(r.OperarioId);
                Assert.NotNull(r.RegistradoEn);
            });
        }

        [Fact]
        public async Task SeedAsync_NoAdminInTheDatabase_FailsClearly()
        {
            // Arrange
            using (var escritura = NuevoContexto())
            {
                escritura.Usuarios.ExecuteDelete();
            }
            using var context = NuevoContexto();
            var seeder = new DemoDataSeeder(context, TimeProvider.System, new LoggerEnMemoria());

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync());
        }

        [Fact]
        public async Task InitializeAsync_ResetOn_SeededDatesEndYesterdayInPlantTime()
        {
            // Arrange
            using var context = NuevoContexto();
            var seeder = new DemoDataSeeder(context, TimeProvider.System);
            var ayer = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Argentina/Buenos_Aires").Date.AddDays(-1);

            // Act
            await seeder.InitializeAsync(resetOnStartup: true, seedOnStartup: false);

            // Assert
            using var lectura = NuevoContexto();
            var fechas = lectura.Recepciones.AsNoTracking().ToList()
                .Select(r => r.FechaHora)
                .ToList();
            Assert.Equal(ayer, fechas.Max().Date);
            Assert.True(fechas.Min().Date >= ayer.AddDays(-DemoDataGenerator.DiasHistorial));
        }

        [Fact]
        public async Task InitializeAsync_ResetOn_SeededDataIncludesWaterValues()
        {
            // Arrange
            using var context = NuevoContexto();
            var seeder = new DemoDataSeeder(context, TimeProvider.System);

            // Act
            await seeder.InitializeAsync(resetOnStartup: true, seedOnStartup: false);

            // Assert
            using var lectura = NuevoContexto();
            var agua = lectura.Recepciones.AsNoTracking().ToList()
                .Select(r => r.Parametros.AguaPorcentaje)
                .ToList();
            Assert.All(agua, a => Assert.NotNull(a));
            Assert.Contains(agua, a => a == 0m);
            Assert.Contains(agua, a => a > 0m);
        }

        [Fact]
        public async Task ResetAndSeedAsync_SeedingFails_RollsBackAndKeepsTheExistingData()
        {
            // Arrange
            CargarDatosViejos();
            using var contexto = new ContextoQueFallaAlGuardar(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
            var seeder = new DemoDataSeeder(contexto, TimeProvider.System);

            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.ResetAndSeedAsync());

            // Assert: the deletes happened inside the transaction, so nothing changed
            var datos = Leer();
            Assert.Equal(["Tambo Viejo"], datos.Nombres);
            Assert.Equal([1], datos.Tickets);
            Assert.Equal(LimitesCalidad.Create(25m, 30m, 5m), datos.Limites);
        }

        [Fact]
        public async Task InitializeAsync_ResetOn_LogsAWarningWithTheDeletedCounts()
        {
            // Arrange
            CargarDatosViejos();
            var logger = new LoggerEnMemoria();
            using var context = NuevoContexto();
            var seeder = new DemoDataSeeder(context, TimeProvider.System, logger);

            // Act
            await seeder.InitializeAsync(resetOnStartup: true, seedOnStartup: false);

            // Assert
            var advertencia = Assert.Single(logger.Entradas, e => e.Nivel == LogLevel.Warning);
            Assert.Contains("deleted 1 proveedores and 1 recepciones", advertencia.Mensaje);
        }

        [Fact]
        public async Task InitializeAsync_ResetOnAnEmptyDatabase_SeedsAndLogsZeroDeleted()
        {
            // Arrange
            var logger = new LoggerEnMemoria();
            using var context = NuevoContexto();
            var seeder = new DemoDataSeeder(context, TimeProvider.System, logger);

            // Act
            await seeder.InitializeAsync(resetOnStartup: true, seedOnStartup: false);

            // Assert
            Assert.Equal(8, Leer().Proveedores);
            var advertencia = Assert.Single(logger.Entradas, e => e.Nivel == LogLevel.Warning);
            Assert.Contains("deleted 0 proveedores and 0 recepciones", advertencia.Mensaje);
        }

        [Fact]
        public async Task InitializeAsync_FlagsOff_LeavesExistingDataUntouched()
        {
            // Arrange
            CargarDatosViejos();
            var logger = new LoggerEnMemoria();
            using var context = NuevoContexto();
            var seeder = new DemoDataSeeder(context, TimeProvider.System, logger);

            // Act
            await seeder.InitializeAsync(resetOnStartup: false, seedOnStartup: false);

            // Assert
            var datos = Leer();
            Assert.Equal(1, datos.Proveedores);
            Assert.Equal(["Tambo Viejo"], datos.Nombres);
            Assert.Equal([1], datos.Tickets);
            Assert.Equal(LimitesCalidad.Create(25m, 30m, 5m), datos.Limites);
            Assert.Empty(logger.Entradas);
        }

        [Fact]
        public async Task InitializeAsync_SeedOnNonEmptyDatabase_LeavesExistingDataUntouched()
        {
            // Arrange
            CargarDatosViejos();
            using var context = NuevoContexto();
            var seeder = new DemoDataSeeder(context, TimeProvider.System);

            // Act
            await seeder.InitializeAsync(resetOnStartup: false, seedOnStartup: true);

            // Assert
            var datos = Leer();
            Assert.Equal(["Tambo Viejo"], datos.Nombres);
            Assert.Equal(LimitesCalidad.Create(25m, 30m, 5m), datos.Limites);
        }

        [Fact]
        public async Task InitializeAsync_SeedOnEmptyDatabase_SeedsTheDemoData()
        {
            // Arrange
            using var context = NuevoContexto();
            var seeder = new DemoDataSeeder(context, TimeProvider.System);

            // Act
            await seeder.InitializeAsync(resetOnStartup: false, seedOnStartup: true);

            // Assert
            var datos = Leer();
            Assert.Equal(8, datos.Proveedores);
            Assert.Equal(Enumerable.Range(4900, datos.Recepciones), datos.Tickets);
            Assert.Equal(LimitesCalidad.Predeterminados, datos.Limites);
        }

        [Fact]
        public async Task ResetAndSeedAsync_AnotherInstanceChangedTheLimits_SkipsTheResetWithAWarningAndKeepsTheData()
        {
            // Arrange
            CargarDatosViejos();
            using var contexto = new ContextoConConflictoDeConcurrencia(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
            var logger = new LoggerEnMemoria();
            var seeder = new DemoDataSeeder(contexto, TimeProvider.System, logger);

            // Act
            await seeder.ResetAndSeedAsync();

            // Assert: no exception reaches the startup, the transaction was rolled back and a warning says why
            var datos = Leer();
            Assert.Equal(["Tambo Viejo"], datos.Nombres);
            Assert.Equal(LimitesCalidad.Create(25m, 30m, 5m), datos.Limites);
            Assert.Contains(logger.Entradas, e => e.Nivel == LogLevel.Warning && e.Mensaje.Contains("skipped"));
        }

        [Fact]
        public async Task ResetAndSeedAsync_FixedClock_StampsTheLimitsAndSeedsRelativeToThatClock()
        {
            // Arrange: 2026-10-15 15:00 UTC is 12:00 in the plant (UTC-3)
            var ahora = new DateTimeOffset(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);
            using var context = NuevoContexto();
            var seeder = new DemoDataSeeder(context, new RelojFijo(ahora));

            // Act
            await seeder.ResetAndSeedAsync();

            // Assert
            using var lectura = NuevoContexto();
            Assert.Equal(ahora.UtcDateTime, lectura.ConfiguracionesCalidad.Single().ActualizadoEn);
            var ultima = lectura.Recepciones.AsNoTracking().ToList().Max(r => r.FechaHora);
            Assert.True(ultima <= new DateTime(2026, 10, 15, 12, 0, 0), $"The demo data reaches {ultima}, past the fixed plant time");
        }
    }
}
