using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
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
    // In-memory SQLite, schema from EnsureCreated. They prove the repository logic (atomic history, the last-Admin check, the PIN index on
    // reactivation, the history query). The isolation level of the transaction and the SQL Server error codes are verified manually
    // against SQL Server (compose stack), as the project documents for everything SQL Server specific.
    public class CuentasProtegidasTests : IDisposable
    {
        private static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);
        private static readonly MotivoDeCambio Motivo = MotivoDeCambio.Create("Cambio de tareas");

        private readonly SqliteConnection _connection;
        private readonly Guid _adminId;

        public CuentasProtegidasTests()
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

        private Usuario Guardar(string username, RolUsuario rol, bool protegido = false)
        {
            using var context = NuevoContexto();
            var usuario = Usuario.Crear(username, username, rol, "hash", debeCambiarPassword: false);
            usuario.MarcarProtegido(protegido);
            context.Usuarios.Add(usuario);
            context.SaveChanges();
            return usuario;
        }

        [Fact]
        public async Task AddAsync_NewUser_SavesTheUserAndItsCreationEntryTogether()
        {
            // Arrange
            using var context = NuevoContexto();
            var usuario = Usuario.Crear("maria", "María", RolUsuario.Oficina, "hash");

            // Act
            await new UsuarioRepository(context).AddAsync(usuario, usuario.RegistrarAlta(_adminId, Ahora));

            // Assert
            using var lectura = NuevoContexto();
            var entrada = lectura.HistorialCambios.AsNoTracking().Single(c => c.EntidadId == usuario.Id);
            Assert.Equal(CambioRegistrado.EntidadUsuario, entrada.Entidad);
            Assert.Equal(CambioRegistrado.Campos.Rol, entrada.Campo);
            Assert.Equal(_adminId, entrada.UsuarioId);
            Assert.Equal(Ahora, entrada.EnUtc);
        }

        [Fact]
        public void Protegido_IsPersistedAndReadBack()
        {
            // Arrange & Act
            var guardado = Guardar("dueno", RolUsuario.Admin, protegido: true);

            // Assert
            using var lectura = NuevoContexto();
            Assert.True(lectura.Usuarios.AsNoTracking().Single(u => u.Id == guardado.Id).Protegido);
            Assert.False(lectura.Usuarios.AsNoTracking().Single(u => u.Id == _adminId).Protegido);
        }

        [Fact]
        public async Task GuardarCambioAsync_DemotingTheLastActiveAdmin_IsRefusedAndNothingIsSaved()
        {
            // Arrange: the only active Admin is _adminId
            using var context = NuevoContexto();
            var repositorio = new UsuarioRepository(context);
            var admin = (await repositorio.GetByIdAsync(_adminId))!;
            var cambio = admin.CambiarRol(RolUsuario.Oficina, Motivo, Guid.NewGuid(), Ahora);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => repositorio.GuardarCambioAsync(admin, cambio, exigirOtroAdminActivo: true));

            // Assert
            Assert.Equal(Usuario.MensajeUltimoAdmin, ex.Message);
            Assert.Equal("id", ex.Campo);
            using var lectura = NuevoContexto();
            Assert.Equal(RolUsuario.Admin, lectura.Usuarios.AsNoTracking().Single(u => u.Id == _adminId).Rol);
            Assert.Empty(lectura.HistorialCambios.AsNoTracking().Where(c => c.EntidadId == _adminId).ToList());
        }

        [Fact]
        public async Task GuardarCambioAsync_DemotingAnAdminWhenAnotherIsActive_SavesTheChangeAndTheEntry()
        {
            // Arrange
            var otro = Guardar("otro", RolUsuario.Admin);
            using var context = NuevoContexto();
            var repositorio = new UsuarioRepository(context);
            var usuario = (await repositorio.GetByIdAsync(otro.Id))!;
            var cambio = usuario.CambiarRol(RolUsuario.Oficina, Motivo, _adminId, Ahora);

            // Act
            await repositorio.GuardarCambioAsync(usuario, cambio, exigirOtroAdminActivo: true);

            // Assert
            using var lectura = NuevoContexto();
            Assert.Equal(RolUsuario.Oficina, lectura.Usuarios.AsNoTracking().Single(u => u.Id == otro.Id).Rol);
            var entrada = lectura.HistorialCambios.AsNoTracking().Single(c => c.EntidadId == otro.Id);
            Assert.Equal("Admin", entrada.ValorAnterior);
            Assert.Equal("Oficina", entrada.ValorNuevo);
            Assert.Equal(_adminId, entrada.UsuarioId);
        }

        [Fact]
        public async Task GuardarCambioAsync_AnInactiveAdminDoesNotCountAsAnotherAdmin()
        {
            // Arrange: the other Admin exists but is deactivated, so _adminId is the last active one
            var inactivo = Guardar("inactivo", RolUsuario.Admin);
            using (var preparar = NuevoContexto())
            {
                var repositorioPreparar = new UsuarioRepository(preparar);
                var u = (await repositorioPreparar.GetByIdAsync(inactivo.Id))!;
                await repositorioPreparar.GuardarCambioAsync(u, u.Desactivar(Motivo, _adminId, Ahora), exigirOtroAdminActivo: false);
            }
            using var context = NuevoContexto();
            var repositorio = new UsuarioRepository(context);
            var admin = (await repositorio.GetByIdAsync(_adminId))!;
            var cambio = admin.Desactivar(Motivo, Guid.NewGuid(), Ahora);

            // Act & Assert
            await Assert.ThrowsAsync<ReglaDeNegocioException>(() => repositorio.GuardarCambioAsync(admin, cambio, exigirOtroAdminActivo: true));
        }

        [Fact]
        public async Task GuardarCambioAsync_WithoutTheLastAdminCheck_SavesTheReactivationAndTheEntry()
        {
            // Arrange
            var otro = Guardar("otro", RolUsuario.Oficina);
            using (var desactivar = NuevoContexto())
            {
                var repo = new UsuarioRepository(desactivar);
                var u = (await repo.GetByIdAsync(otro.Id))!;
                await repo.GuardarCambioAsync(u, u.Desactivar(Motivo, _adminId, Ahora), false);
            }
            using var context = NuevoContexto();
            var repositorio = new UsuarioRepository(context);
            var usuario = (await repositorio.GetByIdAsync(otro.Id))!;
            var sello = usuario.SecurityStamp;

            // Act
            await repositorio.GuardarCambioAsync(usuario, usuario.Reactivar(MotivoDeCambio.Create("Volvió a trabajar"), _adminId, Ahora.AddHours(1)), false);

            // Assert
            using var lectura = NuevoContexto();
            var guardado = lectura.Usuarios.AsNoTracking().Single(u => u.Id == otro.Id);
            Assert.True(guardado.Activo);
            Assert.True(guardado.DebeCambiarPassword);
            Assert.NotEqual(sello, guardado.SecurityStamp);
            Assert.Equal(2, lectura.HistorialCambios.AsNoTracking().Count(c => c.EntidadId == otro.Id));
        }

        [Fact]
        public async Task GuardarCambioAsync_RowChangedInBetween_ThrowsConflictoDeConcurrenciaAndKeepsNoEntry()
        {
            // Arrange: someone saves the user (a login attempt, another admin) after this request read it
            var otro = Guardar("otro", RolUsuario.Oficina);
            using var context = NuevoContexto();
            var repositorio = new UsuarioRepository(context);
            var usuario = (await repositorio.GetByIdAsync(otro.Id))!;
            using (var otroContexto = NuevoContexto())
            {
                await new UsuarioRepository(otroContexto).ModificarAsync(otro.Id, u => u.ReservarIntentoLogin(Ahora));
            }
            var cambio = usuario.Desactivar(Motivo, _adminId, Ahora);

            // Act
            await Assert.ThrowsAsync<ConflictoDeConcurrenciaException>(() => repositorio.GuardarCambioAsync(usuario, cambio, exigirOtroAdminActivo: false));

            // Assert
            using var lectura = NuevoContexto();
            Assert.True(lectura.Usuarios.AsNoTracking().Single(u => u.Id == otro.Id).Activo);
            Assert.Empty(lectura.HistorialCambios.AsNoTracking().Where(c => c.EntidadId == otro.Id).ToList());
        }

        [Fact]
        public async Task OperarioUpdateAsync_ReactivatingWithAPinAnotherActiveOperarioHas_IsRejectedByTheIndexAndKeepsNoEntry()
        {
            // Arrange: "Juan" was deactivated and "Pedro" took the same PIN meanwhile
            Guid juanId;
            using (var preparar = NuevoContexto())
            {
                var juan = Operario.Crear("Juan", "hash-1234");
                juan.Desactivar(Motivo, _adminId, Ahora);
                preparar.Operarios.Add(juan);
                preparar.Operarios.Add(Operario.Crear("Pedro", "hash-1234"));
                preparar.SaveChanges();
                juanId = juan.Id;
            }
            using var context = NuevoContexto();
            var repositorio = new OperarioRepository(context);
            var operario = (await repositorio.GetByIdAsync(juanId))!;
            var cambio = operario.Reactivar(MotivoDeCambio.Create("Volvió a trabajar"), _adminId, Ahora);

            // Act & Assert: the filtered unique index is the last line of defense when the application check races (SQLite has no mapping to ConflictoDeUnicidadException: that SQL Server error code is checked manually, see UnicidadSqlTests)
            await Assert.ThrowsAsync<DbUpdateException>(() => repositorio.UpdateAsync(operario, cambio));
            using var lectura = NuevoContexto();
            Assert.False(lectura.Operarios.AsNoTracking().Single(o => o.Id == juanId).Activo);
            Assert.Empty(lectura.HistorialCambios.AsNoTracking().Where(c => c.EntidadId == juanId).ToList());
        }

        [Fact]
        public async Task OperarioAddAsync_SavesTheOperarioAndItsCreationEntryTogether()
        {
            // Arrange
            using var context = NuevoContexto();
            var operario = Operario.Crear("Juan", "hash-1234");

            // Act
            await new OperarioRepository(context).AddAsync(operario, operario.RegistrarAlta(_adminId, Ahora));

            // Assert
            using var lectura = NuevoContexto();
            Assert.Single(lectura.HistorialCambios.AsNoTracking().Where(c => c.EntidadId == operario.Id && c.Entidad == CambioRegistrado.EntidadOperario).ToList());
        }

        [Fact]
        public async Task OperarioAddAsync_DuplicateActivePin_IsRejectedByTheIndexAndKeepsNoEntry()
        {
            // Arrange
            using (var preparar = NuevoContexto())
            {
                preparar.Operarios.Add(Operario.Crear("Pedro", "hash-1234"));
                preparar.SaveChanges();
            }
            using var context = NuevoContexto();
            var juan = Operario.Crear("Juan", "hash-1234");

            // Act & Assert
            await Assert.ThrowsAsync<DbUpdateException>(() => new OperarioRepository(context).AddAsync(juan, juan.RegistrarAlta(_adminId, Ahora)));
            using var lectura = NuevoContexto();
            Assert.Empty(lectura.HistorialCambios.AsNoTracking().Where(c => c.EntidadId == juan.Id).ToList());
        }

        [Fact]
        public async Task HistorialQueries_UsuarioAndOperario_ReturnTheirEntriesNewestFirstWithTheUserName()
        {
            // Arrange
            var otro = Guardar("otro", RolUsuario.Oficina);
            using (var context = NuevoContexto())
            {
                var repo = new UsuarioRepository(context);
                var u = (await repo.GetByIdAsync(otro.Id))!;
                await repo.GuardarCambioAsync(u, u.RegistrarAlta(_adminId, Ahora), false);
                await repo.GuardarCambioAsync(u, u.CambiarRol(RolUsuario.Consulta, Motivo, _adminId, Ahora.AddHours(1)), false);
                var operario = Operario.Crear("Juan", "hash-1234");
                await new OperarioRepository(context).AddAsync(operario, operario.RegistrarAlta(_adminId, Ahora));
                _operarioId = operario.Id;
            }
            using var lectura = NuevoContexto();
            var consulta = new HistorialQueries(lectura);

            // Act
            var delUsuario = await consulta.GetAsync(CambioRegistrado.EntidadUsuario, otro.Id);
            var delOperario = await consulta.GetAsync(CambioRegistrado.EntidadOperario, _operarioId);
            var desconocido = await consulta.GetAsync(CambioRegistrado.EntidadUsuario, Guid.NewGuid());

            // Assert
            Assert.NotNull(delUsuario);
            Assert.Equal(2, delUsuario.Count);
            Assert.Equal("Consulta", delUsuario[0].Cambios.Single().ValorNuevo); // the role change, newest first
            Assert.Equal("Oficina", delUsuario[1].Cambios.Single().ValorNuevo); // the creation
            Assert.Equal("rol", delUsuario[1].Cambios.Single().Campo);
            Assert.All(delUsuario, c => Assert.Equal("Administrador", c.Usuario));
            Assert.NotNull(delOperario);
            Assert.Single(delOperario);
            Assert.Null(desconocido);
        }

        private Guid _operarioId;

        [Fact]
        public async Task DemoReset_KeepsTheHistoryOfUsuariosAndOperariosAndClearsTheRest()
        {
            // Arrange
            var otro = Guardar("otro", RolUsuario.Oficina);
            Guid proveedorId;
            using (var preparar = NuevoContexto())
            {
                var repo = new UsuarioRepository(preparar);
                var u = (await repo.GetByIdAsync(otro.Id))!;
                await repo.GuardarCambioAsync(u, u.RegistrarAlta(_adminId, Ahora), false);
                var proveedor = Proveedor.Crear("Tambo Viejo", _adminId);
                proveedorId = proveedor.Id;
                preparar.Proveedores.Add(proveedor);
                preparar.HistorialCambios.Add(proveedor.Desactivar(Motivo, _adminId, Ahora));
                preparar.SaveChanges();
            }
            using var context = NuevoContexto();

            // Act
            await new DemoDataSeeder(context, TimeProvider.System, NullLogger<DemoDataSeeder>.Instance).InitializeAsync(resetOnStartup: true, seedOnStartup: false);

            // Assert
            using var lectura = NuevoContexto();
            Assert.Single(lectura.HistorialCambios.AsNoTracking().Where(c => c.Entidad == CambioRegistrado.EntidadUsuario && c.EntidadId == otro.Id).ToList());
            Assert.Empty(lectura.HistorialCambios.AsNoTracking().Where(c => c.EntidadId == proveedorId).ToList());
        }
    }
}
