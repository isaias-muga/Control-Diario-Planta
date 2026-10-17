using System.Data.Common;
using System.Globalization;
using DairyControl.Application.DTOs;
using DairyControl.Application.Settings;
using DairyControl.Application.Services;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;
using DairyControl.Infrastructure.Persistence;
using DairyControl.Infrastructure.Persistence.Queries;
using DairyControl.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using DairyControl.Infrastructure.Security;

namespace DairyControl.Infrastructure.Tests
{
    // Proveedor and RecepcionLeche are separate aggregates: the proveedor reads and registering a recepción
    // never load the history. SQLite does not cover SQL Server translation; that is verified manually.
    public class AgregadosPequenosTests : IDisposable
    {
        private static readonly DateTime Ahora = new(2026, 10, 20, 12, 0, 0);
        private static readonly TimeProvider Reloj = new RelojDePrueba(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));

        private readonly SqliteConnection _connection;
        private readonly Guid _adminId;

        public AgregadosPequenosTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            using var context = NuevoContexto();
            context.Database.EnsureCreated();
            _adminId = UsuariosDePrueba.CrearAdmin(context);
        }

        public void Dispose() => _connection.Dispose();

        private sealed class RelojDePrueba(DateTimeOffset ahora) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => ahora;
        }

        private sealed class CapturaDeSql : DbCommandInterceptor
        {
            public List<string> Comandos { get; } = [];

            public override InterceptionResult<DbDataReader> ReaderExecuting(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            {
                Comandos.Add(command.CommandText);
                return result;
            }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                Comandos.Add(command.CommandText);
                return ValueTask.FromResult(result);
            }
        }

        private AppDbContext NuevoContexto(CapturaDeSql? captura = null)
        {
            var opciones = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection);
            if (captura is not null)
            {
                opciones.AddInterceptors(captura);
            }
            return new AppDbContext(opciones.Options);
        }

        // Many recepciones per proveedor, saved in one batch
        private (Proveedor A, Proveedor B) Cargar(int deA, int deB)
        {
            using var context = NuevoContexto();
            var a = Proveedor.Crear("Tambo A", _adminId);
            var b = Proveedor.Crear("Tambo B", _adminId);
            context.Proveedores.AddRange(a, b);
            var ticket = 1;
            foreach (var (proveedor, cantidad) in new[] { (a, deA), (b, deB) })
            {
                for (var i = 0; i < cantidad; i++)
                {
                    context.Recepciones.Add(RecepcionLeche.Registrar(
                        proveedor.Id, ParametrosCalidad.Create(null, 17m, 5m, 1000m), Ahora.AddMinutes(-ticket), Ahora, 1, null, _adminId, null, Ahora, ticket++));
                }
            }
            context.SaveChanges();
            return (a, b);
        }

        // Only the SELECTs (the INSERT of the new recepción returns its own columns). Reads of Recepciones may only count
        // them or look one up by ticket: never read rows by proveedor, nor any recepción column besides the ticket lookup.
        private static void AssertSinLecturaDeRecepciones(IEnumerable<string> comandos, bool conTicket = false)
        {
            foreach (var sql in comandos.Where(c => c.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)))
            {
                Assert.DoesNotContain("Parametros_Litros", sql);
                Assert.DoesNotContain("Observaciones", sql);
                if (sql.Contains("Recepciones", StringComparison.Ordinal))
                {
                    var esConteo = sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase);
                    var esBusquedaDeTicket = conTicket && sql.Contains("\"Ticket\" =", StringComparison.Ordinal);
                    Assert.True(esConteo || esBusquedaDeTicket, "A query reads recepciones: " + sql);
                }
            }
        }

        [Fact]
        public async Task GetAllAsync_ManyRecepciones_ReturnsCountsComputedInSqlWithoutLoadingRecepciones()
        {
            // Arrange
            var (a, b) = Cargar(250, 40);
            var captura = new CapturaDeSql();
            using var context = NuevoContexto(captura);

            // Act
            var proveedores = await new ProveedorQueries(context).GetAllAsync();

            // Assert
            Assert.Equal(250, proveedores.Single(p => p.Id == a.Id).CantidadRecepciones);
            Assert.Equal(40, proveedores.Single(p => p.Id == b.Id).CantidadRecepciones);
            var sql = Assert.Single(captura.Comandos);
            Assert.Contains("COUNT", sql, StringComparison.OrdinalIgnoreCase);
            AssertSinLecturaDeRecepciones(captura.Comandos);
            Assert.Empty(context.ChangeTracker.Entries<RecepcionLeche>());
        }

        [Fact]
        public async Task GetByIdAsync_ManyRecepciones_ReturnsNombreAndCountWithoutLoadingRecepciones()
        {
            // Arrange
            var (a, _) = Cargar(250, 40);
            var captura = new CapturaDeSql();
            using var context = NuevoContexto(captura);

            // Act
            var proveedor = await new ProveedorQueries(context).GetByIdAsync(a.Id);

            // Assert
            Assert.NotNull(proveedor);
            Assert.Equal("Tambo A", proveedor.Nombre);
            Assert.Equal(250, proveedor.CantidadRecepciones);
            var sql = Assert.Single(captura.Comandos);
            Assert.Contains("COUNT", sql, StringComparison.OrdinalIgnoreCase);
            AssertSinLecturaDeRecepciones(captura.Comandos);
        }

        [Fact]
        public async Task GetByIdAsync_UnknownProveedor_ReturnsNull()
        {
            // Arrange
            using var context = NuevoContexto();

            // Act
            var proveedor = await new ProveedorQueries(context).GetByIdAsync(Guid.NewGuid());

            // Assert
            Assert.Null(proveedor);
        }

        private ProveedorAppService Servicio(AppDbContext context)
        {
            var actual = new UsuarioActualFijo(_adminId, RolUsuario.Admin);
            var pin = new VerificacionPinService(new UsuarioRepository(context), new OperarioRepository(context),
                new PinHasherHmac(Options.Create(new PinSettings { Secret = new string('s', 32) })), actual, Reloj);
            return new(new ProveedorRepository(context), new RecepcionRepository(context), new ProveedorQueries(context), actual, pin, Reloj);
        }

        private static RegistrarRecepcionDto Dto(int? ticket) => new()
        {
            Acidez = 17m,
            Temperatura = 5m,
            Litros = 1000m,
            FechaHora = new DateTime(2026, 10, 20, 11, 0, 0),
            Ticket = ticket
        };

        [Fact]
        public async Task RegistrarRecepcionAsync_ManyRecepciones_NeverReadsOtherRecepciones()
        {
            // Arrange
            var (a, _) = Cargar(250, 40);
            var captura = new CapturaDeSql();
            using var context = NuevoContexto(captura);

            // Act: without a ticket
            var resultado = await Servicio(context).RegistrarRecepcionAsync(a.Id, Dto(null));

            // Assert
            Assert.NotNull(resultado);
            Assert.Equal("Tambo A", resultado.ProveedorNombre);
            AssertSinLecturaDeRecepciones(captura.Comandos);
            Assert.Single(context.ChangeTracker.Entries<RecepcionLeche>()); // only the one just registered is tracked
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_WithTicket_ReadsOnlyTheTicketLookup()
        {
            // Arrange
            var (a, _) = Cargar(250, 40);
            var captura = new CapturaDeSql();
            using var context = NuevoContexto(captura);

            // Act
            var resultado = await Servicio(context).RegistrarRecepcionAsync(a.Id, Dto(9999));

            // Assert
            Assert.NotNull(resultado);
            Assert.Equal("Tambo A", resultado.ProveedorNombre);
            AssertSinLecturaDeRecepciones(captura.Comandos, conTicket: true);
            Assert.Contains(captura.Comandos, c => c.Contains("\"Ticket\" =", StringComparison.Ordinal));
            Assert.Single(context.ChangeTracker.Entries<RecepcionLeche>());
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_UnknownProveedor_ReturnsNullAndWritesNothing()
        {
            // Arrange
            Cargar(3, 3);
            using var context = NuevoContexto();

            // Act
            var resultado = await Servicio(context).RegistrarRecepcionAsync(Guid.NewGuid(), Dto(null));

            // Assert
            Assert.Null(resultado);
            using var lectura = NuevoContexto();
            Assert.Equal(6, lectura.Recepciones.Count());
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_TicketAlreadyUsed_ThrowsTicketDuplicadoWithTheOwner()
        {
            // Arrange: tickets 1..6; Tambo B owns 4..6
            var (a, _) = Cargar(3, 3);
            using var context = NuevoContexto();

            // Act
            var ex = await Assert.ThrowsAsync<TicketDuplicadoException>(() =>
                Servicio(context).RegistrarRecepcionAsync(a.Id, Dto(5)));

            // Assert
            Assert.Contains("Tambo B", ex.Message);
        }

        [Fact]
        public void Model_RecepcionesForeignKey_IsRestrict()
        {
            // Arrange: recepciones are historical records; deleting a proveedor must never delete them
            using var context = NuevoContexto();

            // Act
            var fk = context.Model.FindEntityType(typeof(RecepcionLeche))!
                .GetForeignKeys()
                .Single(f => f.PrincipalEntityType.ClrType == typeof(Proveedor));

            // Assert
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }

        [Fact]
        public void Model_RecepcionesHaveTheDateRangeIndexes()
        {
            // Arrange
            using var context = NuevoContexto();

            // Act
            var indices = context.Model.FindEntityType(typeof(RecepcionLeche))!.GetIndexes()
                .Select(i => string.Join(",", i.Properties.Select(p => p.Name)))
                .ToList();

            // Assert
            Assert.Contains("FechaHora", indices);
            Assert.Contains("ProveedorId,FechaHora", indices);
        }

        [Theory]
        [InlineData("1000", null)]
        [InlineData("1000", "0")]
        [InlineData("1000", "10")]
        [InlineData("1000", "2.5")]
        [InlineData("1905", "1.5")]
        [InlineData("1214.333", null)]
        [InlineData("33.333", "3.7")]
        [InlineData("0.001", "99.99")]
        [InlineData("30000", "100")]
        [InlineData("7777.777", "33.33")]
        public async Task LitrosReales_SqlAndEntity_GiveTheSameRoundedValue(string litros, string? agua)
        {
            // Arrange
            var proveedor = Proveedor.Crear("Tambo A", _adminId);
            var parametros = ParametrosCalidad.Create(
                null, 17m, 5m,
                decimal.Parse(litros, CultureInfo.InvariantCulture),
                agua is null ? null : decimal.Parse(agua, CultureInfo.InvariantCulture));
            var recepcion = RecepcionLeche.Registrar(proveedor.Id, parametros, Ahora.AddHours(-1), Ahora, 1, null, _adminId, null, Ahora, 1);
            using (var escritura = NuevoContexto())
            {
                escritura.Proveedores.Add(proveedor);
                escritura.Recepciones.Add(recepcion);
                escritura.SaveChanges();
            }
            using var context = NuevoContexto();
            var filtro = new FiltroRecepciones { Desde = DateOnly.FromDateTime(Ahora), Hasta = DateOnly.FromDateTime(Ahora) };

            // Act
            var listado = await new RecepcionQueries(context).GetListadoAsync(filtro);

            // Assert
            Assert.Equal(recepcion.LitrosAPagar, listado.Items.Single().LitrosReales);
            Assert.Equal(recepcion.LitrosAPagar, listado.Totales.LitrosReales);
        }
    }
}
