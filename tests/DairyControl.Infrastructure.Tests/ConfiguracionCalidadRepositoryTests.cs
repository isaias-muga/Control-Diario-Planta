using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;
using DairyControl.Infrastructure.Persistence;
using DairyControl.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DairyControl.Infrastructure.Tests
{
    // In-memory SQLite, schema from EnsureCreated. SQL Server fills the rowversion column by itself; SQLite does not,
    // so these tests install a trigger that changes it on every update. They prove the repository's logic (original
    // value, translation to ConflictoDeConcurrenciaException), not the SQL Server rowversion column, which is verified
    // manually against SQL Server.
    public class ConfiguracionCalidadRepositoryTests : IDisposable
    {
        private readonly SqliteConnection _connection;

        public ConfiguracionCalidadRepositoryTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            using var context = NuevoContexto();
            context.Database.EnsureCreated();
            context.Database.ExecuteSqlRaw("UPDATE ConfiguracionesCalidad SET RowVersion = randomblob(8)");
            context.Database.ExecuteSqlRaw(
                "CREATE TRIGGER ConfiguracionesCalidad_RowVersion AFTER UPDATE OF ActualizadoEn, Limites_AcidezMaxima, Limites_TemperaturaMaxima, Limites_AguaMaxima " +
                "ON ConfiguracionesCalidad BEGIN UPDATE ConfiguracionesCalidad SET RowVersion = randomblob(8) WHERE Id = NEW.Id; END;");
        }

        public void Dispose() => _connection.Dispose();

        private AppDbContext NuevoContexto() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

        private static readonly DateTime Ahora = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public async Task GetAsync_SeededRow_ReturnsTheConfigurationWithItsVersion()
        {
            // Arrange
            using var context = NuevoContexto();
            var repository = new ConfiguracionCalidadRepository(context);

            // Act
            var leida = await repository.GetAsync();

            // Assert
            Assert.NotNull(leida);
            Assert.Equal(LimitesCalidad.Predeterminados, leida.Configuracion.Limites);
            Assert.NotEmpty(leida.Version);
        }

        [Fact]
        public async Task UpdateAsync_VersionStillCurrent_SavesTheNewLimits()
        {
            // Arrange
            using var context = NuevoContexto();
            var repository = new ConfiguracionCalidadRepository(context);
            var leida = await repository.GetAsync();
            Assert.NotNull(leida);
            leida.Configuracion.CambiarLimites(LimitesCalidad.Create(25m, 30m, 5m), Ahora);

            // Act
            await repository.UpdateAsync(leida.Configuracion, leida.Version);

            // Assert
            using var lectura = NuevoContexto();
            var guardada = await new ConfiguracionCalidadRepository(lectura).GetAsync();
            Assert.NotNull(guardada);
            Assert.Equal(LimitesCalidad.Create(25m, 30m, 5m), guardada.Configuracion.Limites);
        }

        [Fact]
        public async Task UpdateAsync_VersionReadBeforeSomeoneElseSaved_ThrowsConflictoDeConcurrenciaAndKeepsTheOtherEdit()
        {
            // Arrange: the page of user B loads the limits (version v1); user A saves, so the stored version is now v2.
            // B's request later reads the current row (v2) in a fresh context and sends back the v1 the page had.
            byte[] versionDeB;
            using (var lecturaDeB = NuevoContexto())
            {
                var leida = await new ConfiguracionCalidadRepository(lecturaDeB).GetAsync();
                Assert.NotNull(leida);
                versionDeB = leida.Version;
            }

            using (var contextoA = NuevoContexto())
            {
                var repositorioA = new ConfiguracionCalidadRepository(contextoA);
                var leidaA = await repositorioA.GetAsync();
                Assert.NotNull(leidaA);
                leidaA.Configuracion.CambiarLimites(LimitesCalidad.Create(21m, 22m, 1m), Ahora);
                await repositorioA.UpdateAsync(leidaA.Configuracion, leidaA.Version);
            }

            using var peticionDeB = NuevoContexto();
            var repositorioB = new ConfiguracionCalidadRepository(peticionDeB);
            var actual = await repositorioB.GetAsync();
            Assert.NotNull(actual);
            actual.Configuracion.CambiarLimites(LimitesCalidad.Create(25m, 30m, 5m), Ahora.AddMinutes(1));

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ConflictoDeConcurrenciaException>(
                () => repositorioB.UpdateAsync(actual.Configuracion, versionDeB));
            Assert.Equal("Los límites fueron modificados por otro usuario. Recargá la página.", ex.Message);
            using var lectura = NuevoContexto();
            var guardada = await new ConfiguracionCalidadRepository(lectura).GetAsync();
            Assert.NotNull(guardada);
            Assert.Equal(LimitesCalidad.Create(21m, 22m, 1m), guardada.Configuracion.Limites);
        }

        [Fact]
        public async Task GetAsync_NewDatabase_HasThreeSilos()
        {
            // Arrange
            using var context = NuevoContexto();

            // Act
            var leida = await new ConfiguracionCalidadRepository(context).GetAsync();

            // Assert
            Assert.NotNull(leida);
            Assert.Equal(3, leida.Configuracion.CantidadSilos);
        }

        [Fact]
        public async Task UpdateAsync_NewNumberOfSilos_IsPersisted()
        {
            // Arrange
            using var context = NuevoContexto();
            var repository = new ConfiguracionCalidadRepository(context);
            var leida = await repository.GetAsync();
            Assert.NotNull(leida);
            leida.Configuracion.Actualizar(LimitesCalidad.Predeterminados, 4, Ahora);

            // Act
            await repository.UpdateAsync(leida.Configuracion, leida.Version);

            // Assert
            using var lectura = NuevoContexto();
            var guardada = await new ConfiguracionCalidadRepository(lectura).GetAsync();
            Assert.NotNull(guardada);
            Assert.Equal(4, guardada.Configuracion.CantidadSilos);
        }
    }
}
