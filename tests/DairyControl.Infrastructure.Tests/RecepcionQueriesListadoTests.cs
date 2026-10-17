using DairyControl.Application.DTOs;
using DairyControl.Domain.Entities;
using DairyControl.Domain.ValueObjects;
using DairyControl.Infrastructure.Persistence;
using DairyControl.Infrastructure.Persistence.Queries;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DairyControl.Infrastructure.Tests
{
    // SQLite does not cover SQL Server-specific query translation; the list is also verified manually against SQL Server.
    public class RecepcionQueriesListadoTests : IDisposable
    {
        private static readonly DateTime Ahora = new(2026, 10, 20);

        private readonly SqliteConnection _connection;
        private readonly Guid _adminId;

        public RecepcionQueriesListadoTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            using var context = NuevoContexto();
            context.Database.EnsureCreated();
            UsuariosDePrueba.InstalarVersionDeFila(context);
            _adminId = UsuariosDePrueba.CrearAdmin(context);
        }

        public void Dispose() => _connection.Dispose();

        private AppDbContext NuevoContexto() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

        private static FiltroRecepciones Filtro(string desde, string hasta) => new()
        {
            Desde = DateOnly.Parse(desde),
            Hasta = DateOnly.Parse(hasta)
        };

        private Proveedor AgregarProveedor(string nombre)
        {
            using var context = NuevoContexto();
            var proveedor = Proveedor.Crear(nombre, _adminId);
            context.Proveedores.Add(proveedor);
            context.SaveChanges();
            return proveedor;
        }

        private void Registrar(
            Guid proveedorId,
            DateTime fechaHora,
            int? ticket,
            decimal litros = 1000m,
            decimal acidez = 17m,
            decimal temperatura = 5m,
            decimal? agua = null,
            string? observaciones = null)
        {
            using var context = NuevoContexto();
            context.Recepciones.Add(RecepcionLeche.Registrar(
                proveedorId, ParametrosCalidad.Create(null, acidez, temperatura, litros, agua), fechaHora, Ahora, 1, observaciones, _adminId, null, Ahora, ticket));
            context.SaveChanges();
        }

        [Fact]
        public async Task GetListadoAsync_TwoProveedores_ReturnsNewestFirstWithProveedorName()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            var b = AgregarProveedor("Tambo B");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1);
            Registrar(b.Id, new DateTime(2026, 10, 15, 9, 0, 0), 2);
            Registrar(a.Id, new DateTime(2026, 10, 14, 8, 0, 0), 3);
            using var context = NuevoContexto();
            var queries = new RecepcionQueries(context);

            // Act
            var listado = await queries.GetListadoAsync(Filtro("2026-10-14", "2026-10-15"));

            // Assert
            Assert.Equal([2, 1, 3], listado.Items.Select(i => i.Ticket));
            Assert.Equal("Tambo B", listado.Items[0].ProveedorNombre);
            Assert.Equal(b.Id, listado.Items[0].ProveedorId);
        }

        [Fact]
        public async Task GetListadoAsync_ProveedorFilter_ReturnsOnlyThatProveedor()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            var b = AgregarProveedor("Tambo B");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1);
            Registrar(b.Id, new DateTime(2026, 10, 15, 9, 0, 0), 2);
            using var context = NuevoContexto();
            var filtro = Filtro("2026-10-15", "2026-10-15");
            filtro.ProveedorId = b.Id;

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(filtro);

            // Assert
            Assert.Equal([2], listado.Items.Select(i => i.Ticket));
            Assert.Equal(1, listado.Totales.CantidadRecepciones);
        }

        [Fact]
        public async Task GetListadoAsync_TicketFilter_ReturnsOnlyThatTicket()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 10);
            Registrar(a.Id, new DateTime(2026, 10, 15, 8, 0, 0), 11);
            using var context = NuevoContexto();
            var filtro = Filtro("2026-10-15", "2026-10-15");
            filtro.Ticket = 11;

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(filtro);

            // Assert
            Assert.Equal([11], listado.Items.Select(i => i.Ticket));
        }

        [Fact]
        public async Task GetListadoAsync_SoloFueraDeRango_ReturnsOnlyOutOfRangeWithReasons()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1);
            Registrar(a.Id, new DateTime(2026, 10, 15, 8, 0, 0), 2, temperatura: 21m);
            Registrar(a.Id, new DateTime(2026, 10, 15, 9, 0, 0), 3, acidez: 20m, agua: 1.5m);
            using var context = NuevoContexto();
            var filtro = Filtro("2026-10-15", "2026-10-15");
            filtro.SoloFueraDeRango = true;

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(filtro);

            // Assert
            Assert.Equal([3, 2], listado.Items.Select(i => i.Ticket));
            Assert.Equal([MotivosFueraDeRango.Acidez, MotivosFueraDeRango.Agua], listado.Items[0].Motivos);
            Assert.Equal([MotivosFueraDeRango.Temperatura], listado.Items[1].Motivos);
        }

        [Fact]
        public async Task GetListadoAsync_WithinLimits_HasNoReasons()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1);
            using var context = NuevoContexto();

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-15", "2026-10-15"));

            // Assert
            Assert.Empty(listado.Items[0].Motivos);
        }

        [Fact]
        public async Task GetListadoAsync_EditedLimits_AreAppliedOnEveryRequest()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1, temperatura: 21m);
            using (var contexto = NuevoContexto())
            {
                contexto.ConfiguracionesCalidad.Single().CambiarLimites(LimitesCalidad.Create(19m, 25m, 0m), DateTime.UtcNow);
                contexto.SaveChanges();
            }
            using var context = NuevoContexto();

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-15", "2026-10-15"));

            // Assert
            Assert.Empty(listado.Items[0].Motivos);
            Assert.Equal(0, listado.Totales.CantidadFueraDeRango);
        }

        [Fact]
        public async Task GetListadoAsync_Range_IsHalfOpenInPlantLocalTime()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            Registrar(a.Id, new DateTime(2026, 10, 14, 23, 59, 59), 1);
            Registrar(a.Id, new DateTime(2026, 10, 15, 0, 0, 0), 2);
            Registrar(a.Id, new DateTime(2026, 10, 15, 23, 59, 59), 3);
            Registrar(a.Id, new DateTime(2026, 10, 16, 0, 0, 0), 4);
            using var context = NuevoContexto();

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-15", "2026-10-15"));

            // Assert
            Assert.Equal([3, 2], listado.Items.Select(i => i.Ticket));
        }

        [Fact]
        public async Task GetListadoAsync_Totals_CoverTheWholeFilterAndNotOnlyThePage()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            for (var i = 1; i <= 101; i++)
            {
                Registrar(a.Id, new DateTime(2026, 10, 15, 0, 0, 0).AddMinutes(i), i, litros: 10m,
                    temperatura: i == 1 ? 21m : 5m);
            }
            using var context = NuevoContexto();

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-15", "2026-10-15"));

            // Assert
            Assert.Equal(100, listado.Items.Count);
            Assert.Equal(101, listado.Totales.CantidadRecepciones);
            Assert.Equal(1010m, listado.Totales.Litros);
            Assert.Equal(1, listado.Totales.TicketMinimo);
            Assert.Equal(101, listado.Totales.TicketMaximo);
            Assert.Equal(1, listado.Totales.CantidadFueraDeRango);
            Assert.Equal(1, listado.Totales.FueraPorTemperatura);
            Assert.Equal(0, listado.Totales.FueraPorAcidez);
            Assert.Equal(0, listado.Totales.FueraPorAgua);
        }

        [Fact]
        public async Task GetListadoAsync_LitrosReales_MatchTheDomainLitrosAPagar()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1, litros: 1905m, agua: 1.5m);
            Registrar(a.Id, new DateTime(2026, 10, 15, 8, 0, 0), 2, litros: 1214.333m);
            using var context = NuevoContexto();
            var esperado = context.Recepciones.AsNoTracking().ToList().Sum(r => r.LitrosAPagar);

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-15", "2026-10-15"));

            // Assert
            Assert.Equal(1876.425m, listado.Items.Single(i => i.Ticket == 1).LitrosReales);
            Assert.Equal(1214.333m, listado.Items.Single(i => i.Ticket == 2).LitrosReales);
            Assert.Equal(esperado, listado.Totales.LitrosReales);
        }

        [Fact]
        public async Task GetListadoAsync_MoreThanOnePage_PagesWithoutOverlapAndFlagsHayMas()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            for (var i = 1; i <= 101; i++)
            {
                Registrar(a.Id, new DateTime(2026, 10, 15, 0, 0, 0).AddMinutes(i), i);
            }
            using var context = NuevoContexto();
            var queries = new RecepcionQueries(context);
            var segunda = Filtro("2026-10-15", "2026-10-15");
            segunda.Pagina = 2;

            // Act
            var primeraPagina = await queries.GetListadoAsync(Filtro("2026-10-15", "2026-10-15"));
            var segundaPagina = await queries.GetListadoAsync(segunda);

            // Assert
            Assert.True(primeraPagina.HayMas);
            Assert.Equal(101, primeraPagina.Items.First().Ticket);
            Assert.Equal(2, primeraPagina.Items.Last().Ticket);
            Assert.False(segundaPagina.HayMas);
            Assert.Equal([1], segundaPagina.Items.Select(i => i.Ticket));
            Assert.Equal(100, segundaPagina.TamanoPagina);
        }

        [Fact]
        public async Task GetListadoAsync_ExactlyOnePage_DoesNotFlagHayMas()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            for (var i = 1; i <= 100; i++)
            {
                Registrar(a.Id, new DateTime(2026, 10, 15, 0, 0, 0).AddMinutes(i), i);
            }
            using var context = NuevoContexto();

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-15", "2026-10-15"));

            // Assert
            Assert.False(listado.HayMas);
            Assert.Equal(100, listado.Items.Count);
        }

        [Fact]
        public async Task GetListadoAsync_DaysOnPage_HaveSubtotalsForTheWholeDay()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            Registrar(a.Id, new DateTime(2026, 10, 14, 8, 0, 0), 1, litros: 100m);
            Registrar(a.Id, new DateTime(2026, 10, 15, 8, 0, 0), 2, litros: 200m, temperatura: 21m);
            Registrar(a.Id, new DateTime(2026, 10, 15, 9, 0, 0), 3, litros: 300m);
            Registrar(a.Id, new DateTime(2026, 10, 13, 9, 0, 0), 4, litros: 999m);
            using var context = NuevoContexto();

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-14", "2026-10-15"));

            // Assert
            Assert.Equal(2, listado.Dias.Count);
            Assert.Equal(new DateOnly(2026, 10, 15), listado.Dias[0].Fecha);
            Assert.Equal(2, listado.Dias[0].CantidadRecepciones);
            Assert.Equal(500m, listado.Dias[0].Litros);
            Assert.Equal(1, listado.Dias[0].CantidadFueraDeRango);
            Assert.Equal(new DateOnly(2026, 10, 14), listado.Dias[1].Fecha);
            Assert.Equal(100m, listado.Dias[1].Litros);
            Assert.Equal(0, listado.Dias[1].CantidadFueraDeRango);
        }

        [Fact]
        public async Task GetListadoAsync_SecondPageOfOneDay_SubtotalCoversTheDayBeyondThePage()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            for (var i = 1; i <= 101; i++)
            {
                Registrar(a.Id, new DateTime(2026, 10, 15, 0, 0, 0).AddMinutes(i), i, litros: 10m);
            }
            using var context = NuevoContexto();
            var filtro = Filtro("2026-10-15", "2026-10-15");
            filtro.Pagina = 2;

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(filtro);

            // Assert
            Assert.Single(listado.Items);
            Assert.Equal(101, listado.Dias.Single().CantidadRecepciones);
            Assert.Equal(1010m, listado.Dias.Single().Litros);
        }

        [Fact]
        public async Task GetListadoAsync_NoRecepciones_ReturnsEmptyListWithZeroTotals()
        {
            // Arrange
            AgregarProveedor("Tambo A");
            using var context = NuevoContexto();

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-15", "2026-10-15"));

            // Assert
            Assert.Empty(listado.Items);
            Assert.Empty(listado.Dias);
            Assert.Equal(0, listado.Totales.CantidadRecepciones);
            Assert.Null(listado.Totales.TicketMinimo);
            Assert.False(listado.HayMas);
        }

        [Fact]
        public async Task GetListadoAsync_Observaciones_AreReturned()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1, observaciones: "Olor extraño");
            using var context = NuevoContexto();

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-15", "2026-10-15"));

            // Assert
            Assert.Equal("Olor extraño", listado.Items.Single().Observaciones);
            Assert.Equal(1, listado.Items.Single().Silo);
        }

        [Fact]
        public async Task GetListadoAsync_EqualTimeAndNoTicket_PagesWithoutRepeatingOrSkippingRows()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            for (var i = 0; i < 150; i++)
            {
                Registrar(a.Id, new DateTime(2026, 10, 15, 8, 0, 0), null);
            }
            using var context = NuevoContexto();
            var queries = new RecepcionQueries(context);
            var segunda = Filtro("2026-10-15", "2026-10-15");
            segunda.Pagina = 2;

            // Act
            var primeraPagina = await queries.GetListadoAsync(Filtro("2026-10-15", "2026-10-15"));
            var segundaPagina = await queries.GetListadoAsync(segunda);

            // Assert
            var ids = primeraPagina.Items.Concat(segundaPagina.Items).Select(i => i.Id).ToList();
            Assert.Equal(150, ids.Count);
            Assert.Equal(150, ids.Distinct().Count());
        }

        [Fact]
        public async Task GetSiguienteTicketAsync_LargestPossibleTicketTaken_ReturnsNullInsteadOfWrapping()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), int.MaxValue);
            using var context = NuevoContexto();

            // Act
            var ticket = await new RecepcionQueries(context).GetSiguienteTicketAsync();

            // Assert
            Assert.Null(ticket);
        }

        [Fact]
        public async Task GetSiguienteTicketAsync_NoTickets_ReturnsOne()
        {
            // Arrange
            AgregarProveedor("Tambo A");
            using var context = NuevoContexto();

            // Act
            var ticket = await new RecepcionQueries(context).GetSiguienteTicketAsync();

            // Assert
            Assert.Equal(1, ticket);
        }

        [Fact]
        public async Task GetSiguienteTicketAsync_TicketsAcrossProveedores_ReturnsMaxPlusOne()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            var b = AgregarProveedor("Tambo B");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 4979);
            Registrar(b.Id, new DateTime(2026, 10, 15, 8, 0, 0), 4980);
            Registrar(a.Id, new DateTime(2026, 10, 15, 9, 0, 0), null);
            using var context = NuevoContexto();

            // Act
            var ticket = await new RecepcionQueries(context).GetSiguienteTicketAsync();

            // Assert
            Assert.Equal(4981, ticket);
        }

        [Fact]
        public async Task GetResumenAsync_TwoProveedores_TranslatesAndAggregatesPerProveedor()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            var b = AgregarProveedor("Tambo B");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1, litros: 1000m);
            Registrar(a.Id, new DateTime(2026, 10, 15, 9, 0, 0), 2, litros: 500m, acidez: 25m);
            Registrar(b.Id, new DateTime(2026, 10, 14, 9, 0, 0), 3, litros: 2000m);
            Registrar(b.Id, new DateTime(2026, 10, 13, 9, 0, 0), 4, litros: 300m); // previous period (12 to 13)
            using var context = NuevoContexto();
            var queries = new RecepcionQueries(context);

            // Act
            var resumen = await queries.GetResumenAsync(new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 15), null);

            // Assert
            Assert.Equal(3, resumen.Totales.CantidadRecepciones);
            Assert.Equal(3500m, resumen.Totales.Litros);
            Assert.Equal(1, resumen.Totales.CantidadFueraDeRango);
            Assert.Equal(["Tambo B", "Tambo A"], resumen.Proveedores.Select(p => p.Nombre));
            Assert.Equal(2, resumen.Dias.Count);
            Assert.Equal(300m, resumen.Totales.LitrosPeriodoAnterior);
            Assert.Equal(0, resumen.Totales.CantidadConGrasa);
        }
    }
}
