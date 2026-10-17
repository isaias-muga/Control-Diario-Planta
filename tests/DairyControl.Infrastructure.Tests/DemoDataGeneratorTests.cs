using DairyControl.Domain.Entities;
using DairyControl.Domain.ValueObjects;
using DairyControl.Infrastructure.Persistence;

namespace DairyControl.Infrastructure.Tests
{
    public class DemoDataGeneratorTests
    {
        private static readonly DateTime Desde = new(2026, 4, 15);
        private static readonly Guid AdminId = Guid.NewGuid();

        private static List<RecepcionLeche> Recepciones(DemoDataGenerator.DatosDemo datos) =>
            datos.Recepciones.OrderBy(r => r.FechaHora).ToList();

        [Fact]
        public void Generar_AllProveedores_TicketsAreUniqueAndConsecutiveFrom4900()
        {
            // Arrange
            var generator = new DemoDataGenerator();

            // Act
            var recepciones = Recepciones(generator.Generar(Desde, AdminId));

            // Assert
            Assert.NotEmpty(recepciones);
            Assert.All(recepciones, r => Assert.NotNull(r.Ticket));
            var tickets = recepciones.Select(r => r.Ticket!.Value).OrderBy(t => t).ToList();
            Assert.Equal(Enumerable.Range(4900, recepciones.Count), tickets);
        }

        [Fact]
        public void Generar_AllProveedores_TicketsFollowDeliveryOrder()
        {
            // Arrange
            var generator = new DemoDataGenerator();

            // Act
            var recepciones = Recepciones(generator.Generar(Desde, AdminId));

            // Assert
            var tickets = recepciones.Select(r => r.Ticket!.Value).ToList();
            Assert.Equal(tickets.OrderBy(t => t), tickets);
        }

        [Fact]
        public void Generar_Water_OnlyAllowedBucketsAndAllBucketsPresent()
        {
            // Arrange
            var generator = new DemoDataGenerator();

            // Act
            var agua = Recepciones(generator.Generar(Desde, AdminId))
                .Select(r => r.Parametros.AguaPorcentaje)
                .ToList();

            // Assert
            Assert.All(agua, a => Assert.NotNull(a));
            var sinAgua = agua.Count(a => a == 0m);
            var baja = agua.Count(a => a >= 0.5m && a <= 3m);
            var alta = agua.Count(a => a >= 3.5m && a <= 6m);
            Assert.Equal(agua.Count, sinAgua + baja + alta);
            Assert.True(baja > 0);
            Assert.True(alta > 0);
            Assert.True(sinAgua > agua.Count * 0.9);
        }

        [Fact]
        public void Generar_SameInput_IsDeterministic()
        {
            // Arrange
            var primera = Recepciones(new DemoDataGenerator().Generar(Desde, AdminId));

            // Act
            var segunda = Recepciones(new DemoDataGenerator().Generar(Desde, AdminId));

            // Assert
            Assert.Equal(
                primera.Select(r => (r.FechaHora, r.Ticket, r.Parametros)),
                segunda.Select(r => (r.FechaHora, r.Ticket, r.Parametros)));
        }

        [Fact]
        public void Generar_AllValues_AreInsideThePlausibleRanges()
        {
            // Arrange
            var generator = new DemoDataGenerator();

            // Act
            var recepciones = Recepciones(generator.Generar(Desde, AdminId));

            // Assert
            Assert.NotEmpty(recepciones);
            Assert.All(recepciones, r =>
            {
                Assert.InRange(r.Parametros.Litros, 0.001m, ParametrosCalidad.LitrosMax);
                if (r.Parametros.Grasa is decimal grasa)
                    Assert.InRange(grasa, ParametrosCalidad.GrasaMin, ParametrosCalidad.GrasaMax);
                Assert.InRange(r.Parametros.Acidez, ParametrosCalidad.AcidezMin, ParametrosCalidad.AcidezMax);
                Assert.InRange(r.Parametros.Temperatura, ParametrosCalidad.TemperaturaMin, ParametrosCalidad.TemperaturaMax);
                Assert.InRange(r.Parametros.AguaPorcentaje!.Value, ParametrosCalidad.AguaMinPorcentaje, ParametrosCalidad.AguaMaxPorcentaje);
                Assert.InRange(r.Silo!.Value, RecepcionLeche.SiloMin, RecepcionLeche.SiloMax);
            });
        }
    }
}
