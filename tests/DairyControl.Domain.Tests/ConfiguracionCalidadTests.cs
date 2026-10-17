using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Domain.Tests
{
    public class ConfiguracionCalidadTests
    {
        [Fact]
        public void CambiarLimites_NewLimits_UpdatesLimitsAndTimestamp()
        {
            // Arrange
            var inicial = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            var configuracion = ConfiguracionCalidad.Crear(LimitesCalidad.Create(19m, 20m, 0m), inicial);
            var nuevos = LimitesCalidad.Create(18m, 8m, 1.5m);
            var ahora = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

            // Act
            configuracion.CambiarLimites(nuevos, ahora);

            // Assert
            Assert.Equal(nuevos, configuracion.Limites);
            Assert.Equal(ahora, configuracion.ActualizadoEn);
        }

        [Fact]
        public void CambiarLimites_NullLimits_ThrowsArgumentNullException()
        {
            // Arrange
            var configuracion = ConfiguracionCalidad.Crear(LimitesCalidad.Create(19m, 20m, 0m), DateTime.UtcNow);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => configuracion.CambiarLimites(null!, DateTime.UtcNow));
        }

        private static readonly DateTime Inicial = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Crear_WithoutACount_StartsWithThreeSilos()
        {
            // Act
            var configuracion = ConfiguracionCalidad.Crear(LimitesCalidad.Predeterminados, Inicial);

            // Assert
            Assert.Equal(3, configuracion.CantidadSilos);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(21)]
        public void Crear_CountOutsideThePlausibleRange_ThrowsOnCantidadSilos(int cantidad)
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => ConfiguracionCalidad.Crear(LimitesCalidad.Predeterminados, Inicial, cantidad));

            // Assert
            Assert.Equal("cantidadSilos", ex.Campo);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        [InlineData(20)]
        public void Actualizar_ValidCount_ChangesLimitsCountAndTimestamp(int cantidad)
        {
            // Arrange
            var configuracion = ConfiguracionCalidad.Crear(LimitesCalidad.Predeterminados, Inicial);
            var ahora = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

            // Act
            configuracion.Actualizar(LimitesCalidad.Create(18m, 8m, 1.5m), cantidad, ahora);

            // Assert
            Assert.Equal(cantidad, configuracion.CantidadSilos);
            Assert.Equal(18m, configuracion.Limites.AcidezMaxima);
            Assert.Equal(ahora, configuracion.ActualizadoEn);
        }

        [Fact]
        public void CambiarLimites_KeepsTheNumberOfSilos()
        {
            // Arrange
            var configuracion = ConfiguracionCalidad.Crear(LimitesCalidad.Predeterminados, Inicial, cantidadSilos: 5);

            // Act
            configuracion.CambiarLimites(LimitesCalidad.Create(18m, 8m, 1.5m), Inicial);

            // Assert
            Assert.Equal(5, configuracion.CantidadSilos);
        }

        [Fact]
        public void Actualizar_InvalidCount_ThrowsAndKeepsTheOldOne()
        {
            // Arrange
            var configuracion = ConfiguracionCalidad.Crear(LimitesCalidad.Predeterminados, Inicial, cantidadSilos: 4);

            // Act
            Assert.Throws<ReglaDeNegocioException>(() => configuracion.Actualizar(LimitesCalidad.Predeterminados, 0, Inicial));

            // Assert
            Assert.Equal(4, configuracion.CantidadSilos);
        }

        [Theory]
        [InlineData(1, 3)]
        [InlineData(3, 3)]
        [InlineData(4, 4)]
        public void ValidarSilo_WithinTheCount_IsAccepted(int silo, int cantidad)
        {
            // Arrange
            var configuracion = ConfiguracionCalidad.Crear(LimitesCalidad.Predeterminados, Inicial, cantidad);

            // Act & Assert
            configuracion.ValidarSilo(silo);
        }

        [Theory]
        [InlineData(0, 3)]
        [InlineData(4, 3)]
        [InlineData(21, 20)]
        [InlineData(-2, 3)]
        public void ValidarSilo_OutsideTheCount_ThrowsOnSiloWithTheCountInTheMessage(int silo, int cantidad)
        {
            // Arrange
            var configuracion = ConfiguracionCalidad.Crear(LimitesCalidad.Predeterminados, Inicial, cantidad);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => configuracion.ValidarSilo(silo));

            // Assert
            Assert.Equal("silo", ex.Campo);
            Assert.Equal($"El silo debe estar entre 1 y {cantidad}.", ex.Message);
        }

        [Fact]
        public void ValidarSilo_Missing_ThrowsOnSilo()
        {
            // Arrange
            var configuracion = ConfiguracionCalidad.Crear(LimitesCalidad.Predeterminados, Inicial);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => configuracion.ValidarSilo(null));

            // Assert
            Assert.Equal("silo", ex.Campo);
            Assert.Equal("El silo es obligatorio.", ex.Message);
        }

        [Fact]
        public void ValidarSilo_AfterLoweringTheCount_RejectsTheSilosThatNoLongerExist()
        {
            // Arrange
            var configuracion = ConfiguracionCalidad.Crear(LimitesCalidad.Predeterminados, Inicial, cantidadSilos: 4);
            configuracion.ValidarSilo(4);

            // Act
            configuracion.Actualizar(LimitesCalidad.Predeterminados, 3, Inicial);

            // Assert
            Assert.Throws<ReglaDeNegocioException>(() => configuracion.ValidarSilo(4));
        }
    }
}
