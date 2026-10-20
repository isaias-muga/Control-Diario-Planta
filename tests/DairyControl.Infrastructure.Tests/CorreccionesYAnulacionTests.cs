using DairyControl.Application.DTOs;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;
using DairyControl.Infrastructure.Persistence;
using DairyControl.Infrastructure.Persistence.Queries;
using DairyControl.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DairyControl.Infrastructure.Tests
{
    // Corrections, voids and deactivations against the real repositories and queries on in-memory SQLite. SQLite does not cover
    // SQL Server-specific behavior (the rowversion column, unique-violation error codes, query translation): triggers stand in for the
    // rowversion, and the SQL Server paths are verified manually and by the e2e tests.
    public class CorreccionesYAnulacionTests : IDisposable
    {
        private static readonly DateTime Ahora = new(2026, 10, 20, 12, 0, 0);
        private static readonly DateTime AhoraUtc = new(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);
        private static readonly MotivoDeCambio Motivo = MotivoDeCambio.Create("Error de tipeo en la balanza");

        private readonly SqliteConnection _connection;
        private readonly Guid _adminId;

        public CorreccionesYAnulacionTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            using var context = NuevoContexto();
            context.Database.EnsureCreated();
            UsuariosDePrueba.InstalarVersionDeFila(context);
            UsuariosDePrueba.InstalarVersionDeFilaDeRecepcionesYProveedores(context);
            _adminId = UsuariosDePrueba.CrearAdmin(context);
        }

        public void Dispose() => _connection.Dispose();

        private AppDbContext NuevoContexto() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

        private Proveedor AgregarProveedor(string nombre)
        {
            using var context = NuevoContexto();
            var proveedor = Proveedor.Crear(nombre, _adminId);
            context.Proveedores.Add(proveedor);
            context.SaveChanges();
            return proveedor;
        }

        private Guid Registrar(Guid proveedorId, DateTime fechaHora, int? ticket, decimal litros = 1000m, decimal acidez = 17m)
        {
            using var context = NuevoContexto();
            var recepcion = RecepcionLeche.Registrar(proveedorId, ParametrosCalidad.Create(null, acidez, 5m, litros), fechaHora, Ahora, 1, "Carga", _adminId, null, AhoraUtc, ticket);
            context.Recepciones.Add(recepcion);
            context.SaveChanges();
            return recepcion.Id;
        }

        private static FiltroRecepciones Filtro(string desde, string hasta, bool incluirAnuladas = false) => new()
        {
            Desde = DateOnly.Parse(desde),
            Hasta = DateOnly.Parse(hasta),
            IncluirAnuladas = incluirAnuladas
        };

        private async Task CorregirLitros(Guid id, decimal litros, string motivo = "Error de tipeo en la balanza")
        {
            using var context = NuevoContexto();
            var repo = new RecepcionRepository(context);
            var leida = (await repo.GetByIdAsync(id))!;
            var r = leida.Recepcion;
            var cambios = r.Corregir(r.ProveedorId, "a", "a", ParametrosCalidad.Create(r.Parametros.Grasa, r.Parametros.Acidez, r.Parametros.Temperatura, litros, r.Parametros.AguaPorcentaje),
                r.FechaHora, Ahora, r.Silo, r.Observaciones, r.Ticket, MotivoDeCambio.Create(motivo), _adminId, AhoraUtc);
            await repo.UpdateAsync(r, leida.Version, cambios);
        }

        private async Task Anular(Guid id, string motivo = "Se cargó dos veces")
        {
            using var context = NuevoContexto();
            var repo = new RecepcionRepository(context);
            var leida = (await repo.GetByIdAsync(id))!;
            var cambio = leida.Recepcion.Anular(MotivoDeCambio.Create(motivo), _adminId, AhoraUtc);
            await repo.UpdateAsync(leida.Recepcion, null, [cambio]);
        }

        // ---- the repository: a change and its history are saved together

        [Fact]
        public async Task UpdateAsync_Correction_PersistsTheNewValuesAndTheHistoryInOneSave()
        {
            // Arrange
            var proveedor = AgregarProveedor("Tambo A");
            var id = Registrar(proveedor.Id, new DateTime(2026, 10, 15, 7, 0, 0), 100, litros: 1000m);

            // Act
            await CorregirLitros(id, 1250.5m);

            // Assert: read with a new context, so nothing comes from the tracker
            using var context = NuevoContexto();
            var guardada = await context.Recepciones.AsNoTracking().SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
            Assert.Equal(1250.5m, guardada.Parametros.Litros);
            Assert.Equal(17m, guardada.Parametros.Acidez);
            var cambio = await context.HistorialCambios.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("litros", cambio.Campo);
            Assert.Equal("1000", cambio.ValorAnterior);
            Assert.Equal("1250.5", cambio.ValorNuevo);
            Assert.Equal(id, cambio.EntidadId);
            Assert.Equal(_adminId, cambio.UsuarioId);
        }

        [Fact]
        public async Task UpdateAsync_StaleVersion_ThrowsConflictAndWritesNeitherTheChangeNorTheHistory()
        {
            // Arrange: two admins read the same recepción; the first one saves
            var proveedor = AgregarProveedor("Tambo A");
            var id = Registrar(proveedor.Id, new DateTime(2026, 10, 15, 7, 0, 0), 100, litros: 1000m);
            using var contextoLento = NuevoContexto();
            var repoLento = new RecepcionRepository(contextoLento);
            var leidaPorElLento = (await repoLento.GetByIdAsync(id))!;
            await CorregirLitros(id, 1100m);

            // Act: the second one, with the version it read before, tries to save
            var r = leidaPorElLento.Recepcion;
            var cambios = r.Corregir(r.ProveedorId, "a", "a", ParametrosCalidad.Create(null, 17m, 5m, 1500m), r.FechaHora, Ahora, r.Silo, r.Observaciones, r.Ticket, Motivo, _adminId, AhoraUtc);
            await Assert.ThrowsAsync<ConflictoDeConcurrenciaException>(() => repoLento.UpdateAsync(r, leidaPorElLento.Version, cambios));

            // Assert: only the first correction exists, with only its history
            using var context = NuevoContexto();
            Assert.Equal(1100m, (await context.Recepciones.AsNoTracking().SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken)).Parametros.Litros);
            Assert.Equal(1, await context.HistorialCambios.CountAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsAVersionThatChangesWithEveryUpdate()
        {
            // Arrange
            var proveedor = AgregarProveedor("Tambo A");
            var id = Registrar(proveedor.Id, new DateTime(2026, 10, 15, 7, 0, 0), 100);
            using var antes = NuevoContexto();
            var versionAntes = (await new RecepcionRepository(antes).GetByIdAsync(id))!.Version;

            // Act
            await CorregirLitros(id, 1100m);

            // Assert
            using var despues = NuevoContexto();
            var versionDespues = (await new RecepcionRepository(despues).GetByIdAsync(id))!.Version;
            Assert.False(versionAntes.SequenceEqual(versionDespues));
        }

        [Fact]
        public async Task GetByIdAsync_RecepcionDoesNotExist_ReturnsNull()
        {
            // Arrange
            using var context = NuevoContexto();

            // Act
            var leida = await new RecepcionRepository(context).GetByIdAsync(Guid.NewGuid());

            // Assert
            Assert.Null(leida);
        }

        [Fact]
        public async Task GetTicketRegistradoAsync_VoidedRecepcion_StillReportsTheTicketAsUsedAndSaysItIsVoided()
        {
            // Arrange
            var proveedor = AgregarProveedor("Tambo A");
            var id = Registrar(proveedor.Id, new DateTime(2026, 10, 15, 7, 0, 0), 4980);
            await Anular(id);

            // Act
            using var context = NuevoContexto();
            var registrado = await new RecepcionRepository(context).GetTicketRegistradoAsync(4980);

            // Assert
            Assert.NotNull(registrado);
            Assert.True(registrado.Anulada);
            Assert.Equal("Tambo A", registrado.ProveedorNombre);
        }

        [Fact]
        public async Task UpdateAsync_Void_KeepsTheRowWithItsAuditAndTheTicket()
        {
            // Arrange
            var proveedor = AgregarProveedor("Tambo A");
            var id = Registrar(proveedor.Id, new DateTime(2026, 10, 15, 7, 0, 0), 4980);

            // Act
            await Anular(id, "Se cargó dos veces");

            // Assert
            using var context = NuevoContexto();
            var guardada = await context.Recepciones.AsNoTracking().SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
            Assert.NotNull(guardada.AnuladaEn);
            Assert.Equal(_adminId, guardada.AnuladaPorUsuarioId);
            Assert.Equal("Se cargó dos veces", guardada.MotivoAnulacion);
            Assert.Equal(4980, guardada.Ticket);
            Assert.Equal("anulacion", (await context.HistorialCambios.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).Campo);
        }

        [Fact]
        public async Task ProveedorRepository_RenameAndDeactivate_PersistWithTheirHistoryAndStaleRenameIsRefused()
        {
            // Arrange
            var proveedor = AgregarProveedor("Tambo Don Pedro");
            using var contextoLento = NuevoContexto();
            var repoLento = new ProveedorRepository(contextoLento);
            var leidoPorElLento = (await repoLento.GetLeidoAsync(proveedor.Id))!;

            using (var contexto = NuevoContexto())
            {
                var repo = new ProveedorRepository(contexto);
                var leido = (await repo.GetLeidoAsync(proveedor.Id))!;
                var cambio = leido.Proveedor.Renombrar("Tambo Don Pedro S.A.", Motivo, _adminId, AhoraUtc);
                await repo.UpdateAsync(leido.Proveedor, leido.Version, cambio);
            }
            using (var contexto = NuevoContexto())
            {
                var repo = new ProveedorRepository(contexto);
                var leido = (await repo.GetLeidoAsync(proveedor.Id))!;
                var cambio = leido.Proveedor.Desactivar(Motivo, _adminId, AhoraUtc);
                await repo.UpdateAsync(leido.Proveedor, null, cambio);
            }

            // Act: the slow one renames with the version it read at the start
            var cambioLento = leidoPorElLento.Proveedor.Renombrar("Otro nombre", Motivo, _adminId, AhoraUtc);
            await Assert.ThrowsAsync<ConflictoDeConcurrenciaException>(() => repoLento.UpdateAsync(leidoPorElLento.Proveedor, leidoPorElLento.Version, cambioLento));

            // Assert
            using var context = NuevoContexto();
            var guardado = await context.Proveedores.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Tambo Don Pedro S.A.", guardado.Nombre);
            Assert.False(guardado.Activo);
            Assert.Equal(2, await context.HistorialCambios.CountAsync(TestContext.Current.CancellationToken));
        }

        // ---- the queries leave the voided recepciones out of everything that adds up

        [Fact]
        public async Task GetListadoAsync_VoidedRecepcion_IsLeftOutOfTheItemsAndTheTotalsByDefault()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1, litros: 1000m);
            var anulada = Registrar(a.Id, new DateTime(2026, 10, 15, 8, 0, 0), 2, litros: 5000m, acidez: 25m);
            await Anular(anulada);
            using var context = NuevoContexto();

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-01", "2026-10-31"));

            // Assert
            var item = Assert.Single(listado.Items);
            Assert.Equal(1, item.Ticket);
            Assert.Equal(1000m, listado.Totales.Litros);
            Assert.Equal(1, listado.Totales.CantidadRecepciones);
            Assert.Equal(0, listado.Totales.CantidadFueraDeRango); // the voided one was out of range (acidity 25)
            Assert.Equal(1000m, Assert.Single(listado.Dias).Litros);
        }

        [Fact]
        public async Task GetListadoAsync_IncludeVoided_ShowsItFlaggedWithReasonAndWhoButTheTotalsStillLeaveItOut()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1, litros: 1000m);
            var anulada = Registrar(a.Id, new DateTime(2026, 10, 15, 8, 0, 0), 2, litros: 5000m);
            await Anular(anulada, "Se cargó dos veces");
            using var context = NuevoContexto();

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-01", "2026-10-31", incluirAnuladas: true));

            // Assert
            Assert.Equal(2, listado.Items.Count);
            var voided = listado.Items.Single(i => i.Ticket == 2);
            Assert.True(voided.Anulada);
            Assert.Equal("Se cargó dos veces", voided.MotivoAnulacion);
            Assert.Equal("Administrador", voided.AnuladaPor);
            Assert.Equal(AhoraUtc, voided.AnuladaEn);
            Assert.False(listado.Items.Single(i => i.Ticket == 1).Anulada);
            Assert.Equal(1000m, listado.Totales.Litros);
            Assert.Equal(1, listado.Totales.CantidadRecepciones);
            Assert.Equal(1, Assert.Single(listado.Dias).CantidadRecepciones);
        }

        [Fact]
        public async Task GetListadoAsync_DayWithOnlyVoidedRecepciones_StillGetsAZeroSubtotal()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1, litros: 1000m);
            var anulada = Registrar(a.Id, new DateTime(2026, 10, 14, 8, 0, 0), 2, litros: 5000m);
            await Anular(anulada);
            using var context = NuevoContexto();

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-01", "2026-10-31", incluirAnuladas: true));

            // Assert
            Assert.Equal(2, listado.Dias.Count);
            var dia14 = listado.Dias.Single(d => d.Fecha == new DateOnly(2026, 10, 14));
            Assert.Equal(0, dia14.CantidadRecepciones);
            Assert.Equal(0m, dia14.Litros);
        }

        [Fact]
        public async Task GetListadoAsync_CorrectedRecepcion_ShowsTheNumberOfCorrectionsNotOfFields()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            var id = Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1, litros: 1000m);
            await CorregirLitros(id, 1100m);
            await CorregirLitros(id, 1200m);
            using var context = NuevoContexto();

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-01", "2026-10-31"));

            // Assert
            Assert.Equal(2, Assert.Single(listado.Items).CantidadCorrecciones);
            Assert.Equal(1200m, Assert.Single(listado.Items).Litros);
        }

        [Fact]
        public async Task GetListadoAsync_VoidIsNotCountedAsACorrection()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            var id = Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1);
            await Anular(id);
            using var context = NuevoContexto();

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(Filtro("2026-10-01", "2026-10-31", incluirAnuladas: true));

            // Assert
            Assert.Equal(0, Assert.Single(listado.Items).CantidadCorrecciones);
        }

        [Fact]
        public async Task GetResumenAsync_VoidedRecepcion_IsInNoFigure()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            var b = AgregarProveedor("Tambo B");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1, litros: 1000m);
            var anulada = Registrar(b.Id, new DateTime(2026, 10, 15, 8, 0, 0), 2, litros: 5000m);
            Registrar(a.Id, new DateTime(2026, 9, 28, 8, 0, 0), 3, litros: 700m); // the previous period
            var anuladaAnterior = Registrar(a.Id, new DateTime(2026, 9, 29, 8, 0, 0), 4, litros: 9000m);
            await Anular(anulada);
            await Anular(anuladaAnterior);
            using var context = NuevoContexto();

            // Act
            var resumen = await new RecepcionQueries(context).GetResumenAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null);

            // Assert
            Assert.Equal(1000m, resumen.Totales.Litros);
            Assert.Equal(1, resumen.Totales.CantidadRecepciones);
            Assert.Equal("Tambo A", Assert.Single(resumen.Proveedores).Nombre);
            Assert.Equal(1000m, Assert.Single(resumen.Dias).Litros);
            Assert.Equal(700m, resumen.Totales.LitrosPeriodoAnterior);
        }

        [Fact]
        public async Task GetResumenAsync_CorrectedLitros_ChangeTheTotals()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            var id = Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1, litros: 1000m);
            await CorregirLitros(id, 1250m);
            using var context = NuevoContexto();

            // Act
            var resumen = await new RecepcionQueries(context).GetResumenAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null);

            // Assert
            Assert.Equal(1250m, resumen.Totales.Litros);
        }

        [Fact]
        public async Task GetSiguienteTicketAsync_VoidedRecepcion_StillCountsSoTheNumberIsNotSuggestedAgain()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            var id = Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 4980);
            await Anular(id);
            using var context = NuevoContexto();

            // Act
            var siguiente = await new RecepcionQueries(context).GetSiguienteTicketAsync();

            // Assert
            Assert.Equal(4981, siguiente);
        }

        [Fact]
        public async Task ProveedorQueries_VoidedRecepcion_IsNotCountedAndInactiveIsReportedWithItsVersion()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1);
            var anulada = Registrar(a.Id, new DateTime(2026, 10, 15, 8, 0, 0), 2);
            await Anular(anulada);
            using (var contexto = NuevoContexto())
            {
                var repo = new ProveedorRepository(contexto);
                var leido = (await repo.GetLeidoAsync(a.Id))!;
                await repo.UpdateAsync(leido.Proveedor, null, leido.Proveedor.Desactivar(Motivo, _adminId, AhoraUtc));
            }
            using var context = NuevoContexto();

            // Act
            var dto = await new ProveedorQueries(context).GetByIdAsync(a.Id);
            var todos = await new ProveedorQueries(context).GetAllAsync();

            // Assert
            Assert.NotNull(dto);
            Assert.Equal(1, dto.CantidadRecepciones);
            Assert.False(dto.Activo);
            Assert.False(string.IsNullOrEmpty(dto.Version));
            Assert.Single(todos);
        }

        // ---- the history

        [Fact]
        public async Task HistorialQueries_RecepcionWithCorrections_ReturnsOneItemPerCorrectionNewestFirstWithUserAndReason()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            var id = Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1, litros: 1000m);
            await CorregirLitros(id, 1100m, "Primer motivo");
            await Task.Delay(5, TestContext.Current.CancellationToken);
            await CorregirLitros(id, 1200m, "Segundo motivo");
            using var context = NuevoContexto();

            // Act
            var historial = await new HistorialQueries(context).GetAsync(CambioRegistrado.EntidadRecepcion, id);

            // Assert
            Assert.NotNull(historial);
            Assert.Equal(2, historial.Count);
            Assert.Equal("Administrador", historial[0].Usuario);
            var cambios = historial.SelectMany(h => h.Cambios).ToList();
            Assert.Equal(2, cambios.Count);
            Assert.Contains(historial, h => h.Motivo == "Primer motivo" && h.Cambios.Single().ValorAnterior == "1000" && h.Cambios.Single().ValorNuevo == "1100");
            Assert.Contains(historial, h => h.Motivo == "Segundo motivo" && h.Cambios.Single().ValorAnterior == "1100" && h.Cambios.Single().ValorNuevo == "1200");
        }

        [Fact]
        public async Task HistorialQueries_EntityWithoutChanges_ReturnsAnEmptyList()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            using var context = NuevoContexto();

            // Act
            var historial = await new HistorialQueries(context).GetAsync(CambioRegistrado.EntidadProveedor, a.Id);

            // Assert
            Assert.NotNull(historial);
            Assert.Empty(historial);
        }

        [Fact]
        public async Task HistorialQueries_EntityDoesNotExist_ReturnsNull()
        {
            // Arrange
            using var context = NuevoContexto();

            // Act & Assert
            Assert.Null(await new HistorialQueries(context).GetAsync(CambioRegistrado.EntidadRecepcion, Guid.NewGuid()));
            Assert.Null(await new HistorialQueries(context).GetAsync(CambioRegistrado.EntidadProveedor, Guid.NewGuid()));
            Assert.Null(await new HistorialQueries(context).GetAsync("Otra", Guid.NewGuid()));
        }

        [Fact]
        public async Task HistorialQueries_HistoryOfOneRecepcion_NeverMixesWithAnother()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            var uno = Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1);
            var dos = Registrar(a.Id, new DateTime(2026, 10, 15, 8, 0, 0), 2);
            await CorregirLitros(uno, 1100m);
            using var context = NuevoContexto();

            // Act
            var historialDos = await new HistorialQueries(context).GetAsync(CambioRegistrado.EntidadRecepcion, dos);

            // Assert
            Assert.NotNull(historialDos);
            Assert.Empty(historialDos);
        }

        // ---- the demo reset is the one place that deletes

        [Fact]
        public async Task DemoReset_AlsoClearsTheHistoryOfWhatItDeletes()
        {
            // Arrange
            var a = AgregarProveedor("Tambo A");
            var id = Registrar(a.Id, new DateTime(2026, 10, 15, 7, 0, 0), 1);
            await CorregirLitros(id, 1100m);
            using var context = NuevoContexto();
            var seeder = new DemoDataSeeder(context, TimeProvider.System, NullLogger<DemoDataSeeder>.Instance);

            // Act
            await seeder.InitializeAsync(resetOnStartup: true, seedOnStartup: false);

            // Assert
            using var despues = NuevoContexto();
            Assert.Empty(await despues.HistorialCambios.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));
            // The reset reseeds the demo data, so what must be gone is the recepción that had the history
            Assert.False(await despues.Recepciones.AsNoTracking().AnyAsync(r => r.Id == id, TestContext.Current.CancellationToken));
        }
    }
}
