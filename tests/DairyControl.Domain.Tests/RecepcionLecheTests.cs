using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Entities;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Domain.Tests
{
    public class RecepcionLecheTests
    {
        private static readonly Guid ProveedorId = Guid.NewGuid();
        private static readonly DateTime Ahora = new(2026, 10, 15, 12, 0, 0);
        private static readonly DateTime Fecha = new(2026, 10, 1, 6, 0, 0);
        private static readonly Guid UsuarioId = Guid.NewGuid();
        private static readonly DateTime AhoraUtc = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);

        private static RecepcionLeche Registrar(decimal litros, decimal? agua, int? ticket = null)
        {
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, litros, agua);
            return RecepcionLeche.Registrar(ProveedorId, parametros, Fecha, Ahora, 1, null, UsuarioId, null, AhoraUtc, ticket);
        }

        [Fact]
        public void Registrar_WithAuditData_StoresWhoAndWhenRegistered()
        {
            // Arrange
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, 1000m);
            var operarioId = Guid.NewGuid();

            // Act
            var recepcion = RecepcionLeche.Registrar(ProveedorId, parametros, Fecha, Ahora, 1, null, UsuarioId, operarioId, AhoraUtc);

            // Assert
            Assert.Equal(UsuarioId, recepcion.RegistradoPorUsuarioId);
            Assert.Equal(operarioId, recepcion.OperarioId);
            Assert.Equal(AhoraUtc, recepcion.RegistradoEn);
        }

        [Fact]
        public void Registrar_EmptyUsuarioId_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, 1000m);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() =>
                RecepcionLeche.Registrar(ProveedorId, parametros, Fecha, Ahora, 1, null, Guid.Empty, null, AhoraUtc));

            // Assert
            Assert.Equal("registradoPorUsuarioId", ex.Campo);
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void Registrar_BlankObservaciones_StoresNull(string observaciones)
        {
            // Arrange
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, 1000m, null);

            // Act
            var recepcion = RecepcionLeche.Registrar(ProveedorId, parametros, Fecha, Ahora, 1, observaciones, UsuarioId, null, AhoraUtc);

            // Assert
            Assert.Null(recepcion.Observaciones);
        }

        [Theory]
        [InlineData(1000, 0, 1000)]
        [InlineData(1000, 10, 900)]
        [InlineData(1000, 2.5, 975)]
        [InlineData(1000, 100, 0)]
        public void LitrosAPagar_WaterPercentage_DeductsWater(double litros, double agua, double esperado)
        {
            // Arrange
            var recepcion = Registrar((decimal)litros, (decimal)agua);

            // Act
            var result = recepcion.LitrosAPagar;

            // Assert
            Assert.Equal((decimal)esperado, result);
        }

        [Fact]
        public void LitrosAPagar_WaterNull_TreatsWaterAsZero()
        {
            // Arrange
            var recepcion = Registrar(1000m, null);

            // Act
            var result = recepcion.LitrosAPagar;

            // Assert
            Assert.Equal(1000m, result);
        }

        [Fact]
        public void Registrar_ValidTicket_StoresTicket()
        {
            // Act
            var recepcion = Registrar(1000m, null, 1234);

            // Assert
            Assert.Equal(1234, recepcion.Ticket);
        }

        [Fact]
        public void Registrar_NoTicket_TicketIsNull()
        {
            // Act
            var recepcion = Registrar(1000m, null);

            // Assert
            Assert.Null(recepcion.Ticket);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        public void Registrar_NonPositiveTicket_ThrowsReglaDeNegocioException(int ticket)
        {
            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => Registrar(1000m, null, ticket));

            // Assert
            Assert.Equal("ticket", ex.Campo);
        }

        // Silo: 1 to 20

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(20)]
        public void Registrar_SiloWithinRange_StoresSilo(int silo)
        {
            // Arrange
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, 1000m);

            // Act
            var recepcion = RecepcionLeche.Registrar(ProveedorId, parametros, Fecha, Ahora, silo, null, UsuarioId, null, AhoraUtc);

            // Assert
            Assert.Equal(silo, recepcion.Silo);
        }

        [Fact]
        public void Registrar_NoSilo_SiloIsNull()
        {
            // Arrange
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, 1000m);

            // Act
            var recepcion = RecepcionLeche.Registrar(ProveedorId, parametros, Fecha, Ahora, null, null, UsuarioId, null, AhoraUtc);

            // Assert
            Assert.Null(recepcion.Silo);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(21)]
        [InlineData(99)]
        public void Registrar_SiloOutOfRange_ThrowsReglaDeNegocioException(int silo)
        {
            // Arrange
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, 1000m);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() =>
                RecepcionLeche.Registrar(ProveedorId, parametros, Fecha, Ahora, silo, null, UsuarioId, null, AhoraUtc));

            // Assert
            Assert.Equal("silo", ex.Campo);
            Assert.Contains("El silo debe estar entre 1 y 20.", ex.Message);
        }

        [Fact]
        public void Registrar_FechaHoraOneSecondOverTheLimit_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, 1000m);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() =>
                RecepcionLeche.Registrar(ProveedorId, parametros, Ahora.AddHours(1).AddSeconds(1), Ahora, 1, null, UsuarioId, null, AhoraUtc));

            // Assert
            Assert.Equal("fechaHora", ex.Campo);
        }

        // FechaHora: required, from 2000-01-01 to now + 1 hour (plant time)

        [Fact]
        public void Registrar_DefaultFechaHora_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, 1000m);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() =>
                RecepcionLeche.Registrar(ProveedorId, parametros, default, Ahora, 1, null, UsuarioId, null, AhoraUtc));

            // Assert
            Assert.Equal("fechaHora", ex.Campo);
            Assert.Contains("obligatoria", ex.Message);
        }

        [Fact]
        public void Registrar_FechaHoraBeforeYear2000_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, 1000m);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() =>
                RecepcionLeche.Registrar(ProveedorId, parametros, new DateTime(1999, 12, 31, 23, 59, 0), Ahora, 1, null, UsuarioId, null, AhoraUtc));

            // Assert
            Assert.Equal("fechaHora", ex.Campo);
            Assert.Contains("01/01/2000", ex.Message);
        }

        [Fact]
        public void Registrar_FechaHoraOnMinimum_StoresFechaHora()
        {
            // Arrange
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, 1000m);

            // Act
            var recepcion = RecepcionLeche.Registrar(ProveedorId, parametros, new DateTime(2000, 1, 1), Ahora, 1, null, UsuarioId, null, AhoraUtc);

            // Assert
            Assert.Equal(new DateTime(2000, 1, 1), recepcion.FechaHora);
        }

        [Fact]
        public void Registrar_FechaHoraWithinTheNextHour_StoresFechaHora()
        {
            // Arrange
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, 1000m);

            // Act
            var recepcion = RecepcionLeche.Registrar(ProveedorId, parametros, Ahora.AddHours(1), Ahora, 1, null, UsuarioId, null, AhoraUtc);

            // Assert
            Assert.Equal(Ahora.AddHours(1), recepcion.FechaHora);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(60 * 24 * 365 * 70)]
        public void Registrar_FechaHoraMoreThanOneHourAhead_ThrowsReglaDeNegocioException(int minutosExtra)
        {
            // Arrange
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, 1000m);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() =>
                RecepcionLeche.Registrar(ProveedorId, parametros, Ahora.AddHours(1).AddMinutes(minutosExtra), Ahora, 1, null, UsuarioId, null, AhoraUtc));

            // Assert
            Assert.Equal("fechaHora", ex.Campo);
            Assert.Contains("hora actual", ex.Message);
        }

        [Fact]
        public void Registrar_ValidProveedorId_StoresProveedorId()
        {
            // Act
            var recepcion = Registrar(1000m, null);

            // Assert
            Assert.Equal(ProveedorId, recepcion.ProveedorId);
        }

        [Fact]
        public void Registrar_EmptyProveedorId_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var parametros = ParametrosCalidad.Create(null, 18m, 4m, 1000m);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() =>
                RecepcionLeche.Registrar(Guid.Empty, parametros, Fecha, Ahora, 1, null, UsuarioId, null, AhoraUtc));

            // Assert
            Assert.Equal("proveedorId", ex.Campo);
        }

        [Fact]
        public void Registrar_NullParametros_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                RecepcionLeche.Registrar(ProveedorId, null!, Fecha, Ahora, 1, null, UsuarioId, null, AhoraUtc));
        }

        [Theory]
        [InlineData(0.001, null, 0.001)]
        [InlineData(33.333, 3.7, 32.1)] // 33.333 * 0.963 = 32.099679 -> 3 decimals
        [InlineData(30000.0, 100.0, 0.0)]
        [InlineData(1234.567, 0.0, 1234.567)]
        public void LitrosAPagarRegla_ExpressionMatchesEntityBeforeRounding(double litros, double? agua, double esperado)
        {
            // Arrange
            var recepcion = Registrar((decimal)litros, (decimal?)agua);
            var formula = DairyControl.Domain.Rules.LitrosAPagarRegla.Expresion<ParametrosCalidad>(p => p.Litros, p => p.AguaPorcentaje).Compile();

            // Act
            var sinRedondear = formula(recepcion.Parametros);

            // Assert
            Assert.Equal((decimal)esperado, recepcion.LitrosAPagar);
            Assert.Equal((decimal)esperado, Math.Round(sinRedondear, ParametrosCalidad.ScaleLitros));
        }

        [Fact]
        public void Expandir_ReplacesEveryDeCallWithTheFormula()
        {
            // Arrange
            System.Linq.Expressions.Expression<Func<RecepcionLeche, decimal>> consulta = r => DairyControl.Domain.Rules.LitrosAPagarRegla.De(r) + 1m;

            // Act
            var expandida = DairyControl.Domain.Rules.LitrosAPagarRegla.Expandir(consulta);

            // Assert: no call is left (EF Core could not translate it) and the result is the same value
            Assert.DoesNotContain("LitrosAPagarRegla", expandida.ToString());
            var recepcion = Registrar(1000m, 10m);
            Assert.Equal(901m, expandida.Compile()(recepcion));
        }
    }
}
