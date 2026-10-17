using DairyControl.Domain.Exceptions;
using DairyControl.Application.DTOs;
using DairyControl.Application.Services;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Interfaces;
using DairyControl.Domain.ValueObjects;
using Moq;

namespace DairyControl.Application.Tests
{
    public class ConfiguracionCalidadAppServiceTests
    {
        private static readonly DateTimeOffset Ahora = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);
        private static readonly TimeProvider Reloj = new RelojFijo(Ahora);

        private static readonly byte[] VersionLeida = [0, 0, 0, 0, 0, 0, 7, 208];
        private static readonly byte[] VersionNueva = [0, 0, 0, 0, 0, 0, 7, 209];
        private static readonly string VersionLeidaBase64 = Convert.ToBase64String(VersionLeida);

        private static ConfiguracionCalidad Configuracion(DateTime? actualizadoEn = null) =>
            ConfiguracionCalidad.Crear(
                LimitesCalidad.Create(19m, 20m, 0m),
                actualizadoEn ?? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        private static ActualizarLimitesCalidadDto Dto(string? version, decimal acidez = 18m, int cantidadSilos = 3) =>
            new() { AcidezMaxima = acidez, TemperaturaMaxima = 8m, AguaMaxima = 1.5m, CantidadSilos = cantidadSilos, Version = version };

        [Fact]
        public async Task GetAsync_ConfiguracionExists_ReturnsLimitsLastUpdateAndVersion()
        {
            // Arrange
            var actualizadoEn = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            var mockRepo = new Mock<IConfiguracionCalidadRepository>();
            mockRepo.Setup(r => r.GetAsync()).ReturnsAsync(new ConfiguracionLeida(Configuracion(actualizadoEn), VersionLeida));
            var service = new ConfiguracionCalidadAppService(mockRepo.Object, Reloj);

            // Act
            var result = await service.GetAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(19m, result.AcidezMaxima);
            Assert.Equal(20m, result.TemperaturaMaxima);
            Assert.Equal(0m, result.AguaMaxima);
            Assert.Equal(actualizadoEn, result.ActualizadoEn);
            Assert.Equal(VersionLeidaBase64, result.Version);
            mockRepo.Verify(r => r.UpdateAsync(It.IsAny<ConfiguracionCalidad>(), It.IsAny<byte[]>()), Times.Never);
        }

        [Fact]
        public async Task GetAsync_ConfiguracionMissing_ReturnsNull()
        {
            // Arrange
            var mockRepo = new Mock<IConfiguracionCalidadRepository>();
            mockRepo.Setup(r => r.GetAsync()).ReturnsAsync((ConfiguracionLeida?)null);
            var service = new ConfiguracionCalidadAppService(mockRepo.Object, Reloj);

            // Act
            var result = await service.GetAsync();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ActualizarAsync_ValidLimitsAndVersion_UpdatesWithTheVersionReadAndReturnsTheNewOne()
        {
            // Arrange
            var configuracion = Configuracion();
            var mockRepo = new Mock<IConfiguracionCalidadRepository>();
            mockRepo.Setup(r => r.GetAsync()).ReturnsAsync(new ConfiguracionLeida(configuracion, VersionNueva));
            mockRepo.Setup(r => r.UpdateAsync(configuracion, It.IsAny<byte[]>())).ReturnsAsync(VersionNueva);
            var service = new ConfiguracionCalidadAppService(mockRepo.Object, Reloj);

            // Act
            var result = await service.ActualizarAsync(Dto(VersionLeidaBase64));

            // Assert
            Assert.NotNull(result);
            Assert.Equal(18m, result.AcidezMaxima);
            Assert.Equal(8m, result.TemperaturaMaxima);
            Assert.Equal(1.5m, result.AguaMaxima);
            Assert.Equal(Ahora.UtcDateTime, result.ActualizadoEn);
            Assert.Equal(Convert.ToBase64String(VersionNueva), result.Version);
            Assert.Equal(18m, configuracion.Limites.AcidezMaxima);
            mockRepo.Verify(r => r.UpdateAsync(configuracion, It.Is<byte[]>(v => v.SequenceEqual(VersionLeida))), Times.Once);
        }

        [Fact]
        public async Task ActualizarAsync_StaleVersion_PropagatesTheConflict()
        {
            // Arrange
            var configuracion = Configuracion();
            var mockRepo = new Mock<IConfiguracionCalidadRepository>();
            mockRepo.Setup(r => r.GetAsync()).ReturnsAsync(new ConfiguracionLeida(configuracion, VersionNueva));
            mockRepo.Setup(r => r.UpdateAsync(configuracion, It.IsAny<byte[]>())).ThrowsAsync(new ConflictoDeConcurrenciaException());
            var service = new ConfiguracionCalidadAppService(mockRepo.Object, Reloj);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ConflictoDeConcurrenciaException>(() => service.ActualizarAsync(Dto(VersionLeidaBase64)));
            Assert.Equal("Los límites fueron modificados por otro usuario. Recargá la página.", ex.Message);
            mockRepo.Verify(r => r.UpdateAsync(configuracion, It.IsAny<byte[]>()), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task ActualizarAsync_MissingVersion_ThrowsReglaDeNegocioAndNeverTouchesTheRepository(string? version)
        {
            // Arrange
            var mockRepo = new Mock<IConfiguracionCalidadRepository>();
            var service = new ConfiguracionCalidadAppService(mockRepo.Object, Reloj);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => service.ActualizarAsync(Dto(version)));
            Assert.Equal("version", ex.Campo);
            Assert.Equal("Falta la versión de los límites. Recargá la página.", ex.Message);
            mockRepo.Verify(r => r.GetAsync(), Times.Never);
            mockRepo.Verify(r => r.UpdateAsync(It.IsAny<ConfiguracionCalidad>(), It.IsAny<byte[]>()), Times.Never);
        }

        [Fact]
        public async Task ActualizarAsync_VersionNotBase64_ThrowsReglaDeNegocioAndNeverCallsUpdate()
        {
            // Arrange
            var mockRepo = new Mock<IConfiguracionCalidadRepository>();
            var service = new ConfiguracionCalidadAppService(mockRepo.Object, Reloj);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => service.ActualizarAsync(Dto("esto no es base64!")));
            Assert.Equal("version", ex.Campo);
            mockRepo.Verify(r => r.UpdateAsync(It.IsAny<ConfiguracionCalidad>(), It.IsAny<byte[]>()), Times.Never);
        }

        [Fact]
        public async Task ActualizarAsync_InvalidLimits_ThrowsAndNeverCallsUpdate()
        {
            // Arrange
            var mockRepo = new Mock<IConfiguracionCalidadRepository>();
            mockRepo.Setup(r => r.GetAsync()).ReturnsAsync(new ConfiguracionLeida(Configuracion(), VersionLeida));
            var service = new ConfiguracionCalidadAppService(mockRepo.Object, Reloj);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => service.ActualizarAsync(Dto(VersionLeidaBase64, acidez: 5m)));
            Assert.Equal("acidezMaxima", ex.Campo);
            mockRepo.Verify(r => r.UpdateAsync(It.IsAny<ConfiguracionCalidad>(), It.IsAny<byte[]>()), Times.Never);
        }

        [Fact]
        public async Task ActualizarAsync_ConfiguracionMissing_ReturnsNullAndNeverCallsUpdate()
        {
            // Arrange
            var mockRepo = new Mock<IConfiguracionCalidadRepository>();
            mockRepo.Setup(r => r.GetAsync()).ReturnsAsync((ConfiguracionLeida?)null);
            var service = new ConfiguracionCalidadAppService(mockRepo.Object, Reloj);

            // Act
            var result = await service.ActualizarAsync(Dto(VersionLeidaBase64, acidez: 19m));

            // Assert
            Assert.Null(result);
            mockRepo.Verify(r => r.UpdateAsync(It.IsAny<ConfiguracionCalidad>(), It.IsAny<byte[]>()), Times.Never);
        }

        [Fact]
        public async Task GetAsync_ReturnsTheNumberOfSilos()
        {
            // Arrange
            var mockRepo = new Mock<IConfiguracionCalidadRepository>();
            mockRepo.Setup(r => r.GetAsync()).ReturnsAsync(new ConfiguracionLeida(ConfiguracionCalidad.Crear(LimitesCalidad.Predeterminados, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), 5), VersionLeida));

            // Act
            var result = await new ConfiguracionCalidadAppService(mockRepo.Object, Reloj).GetAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(5, result.CantidadSilos);
        }

        [Fact]
        public async Task ActualizarAsync_NewNumberOfSilos_IsSavedAndReturned()
        {
            // Arrange
            var configuracion = Configuracion();
            var mockRepo = new Mock<IConfiguracionCalidadRepository>();
            mockRepo.Setup(r => r.GetAsync()).ReturnsAsync(new ConfiguracionLeida(configuracion, VersionLeida));
            mockRepo.Setup(r => r.UpdateAsync(configuracion, It.IsAny<byte[]>())).ReturnsAsync(VersionNueva);

            // Act
            var result = await new ConfiguracionCalidadAppService(mockRepo.Object, Reloj).ActualizarAsync(Dto(VersionLeidaBase64, cantidadSilos: 4));

            // Assert
            Assert.NotNull(result);
            Assert.Equal(4, result.CantidadSilos);
            Assert.Equal(4, configuracion.CantidadSilos);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(21)]
        public async Task ActualizarAsync_InvalidNumberOfSilos_ThrowsOnCantidadSilosAndNeverSaves(int cantidad)
        {
            // Arrange
            var configuracion = Configuracion();
            var mockRepo = new Mock<IConfiguracionCalidadRepository>();
            mockRepo.Setup(r => r.GetAsync()).ReturnsAsync(new ConfiguracionLeida(configuracion, VersionLeida));

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() =>
                new ConfiguracionCalidadAppService(mockRepo.Object, Reloj).ActualizarAsync(Dto(VersionLeidaBase64, cantidadSilos: cantidad)));

            // Assert
            Assert.Equal("cantidadSilos", ex.Campo);
            mockRepo.Verify(r => r.UpdateAsync(It.IsAny<ConfiguracionCalidad>(), It.IsAny<byte[]>()), Times.Never);
        }
    }
}
