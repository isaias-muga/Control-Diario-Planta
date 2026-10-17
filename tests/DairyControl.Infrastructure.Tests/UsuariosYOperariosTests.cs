using DairyControl.Application.Settings;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.ValueObjects;
using DairyControl.Infrastructure.Persistence;
using DairyControl.Infrastructure.Persistence.Repositories;
using DairyControl.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DairyControl.Infrastructure.Tests
{
    // In-memory SQLite, schema from EnsureCreated. The unique-violation error numbers of SQL Server are not covered here
    // (see UnicidadSqlTests): these tests prove that the indexes exist and what they cover.
    public class UsuariosYOperariosTests : IDisposable
    {
        private readonly SqliteConnection _connection;

        public UsuariosYOperariosTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            using var context = NuevoContexto();
            context.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private AppDbContext NuevoContexto() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

        [Fact]
        public async Task GetByUsernameAsync_ExistingUser_ReturnsItWithItsRole()
        {
            // Arrange
            using (var escritura = NuevoContexto())
            {
                escritura.Usuarios.Add(Usuario.Crear("maria", "María", RolUsuario.Recepcion, "hash"));
                escritura.SaveChanges();
            }
            using var context = NuevoContexto();

            // Act
            var usuario = await new UsuarioRepository(context).GetByUsernameAsync("maria");

            // Assert
            Assert.NotNull(usuario);
            Assert.Equal(RolUsuario.Recepcion, usuario.Rol);
            Assert.True(usuario.DebeCambiarPassword);
        }

        [Fact]
        public async Task UpdateAsync_AfterRegisteringAFailure_PersistsTheCounters()
        {
            // Arrange
            using (var escritura = NuevoContexto())
            {
                escritura.Usuarios.Add(Usuario.Crear("maria", "María", RolUsuario.Recepcion, "hash"));
                escritura.SaveChanges();
            }
            var ahora = new DateTime(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);
            using (var context = NuevoContexto())
            {
                var repositorio = new UsuarioRepository(context);
                var usuario = (await repositorio.GetByUsernameAsync("maria"))!;
                for (var i = 0; i < Usuario.MaxFallosPin; i++)
                {
                    usuario.RegistrarPinIncorrecto(ahora);
                }

                // Act
                await repositorio.UpdateAsync(usuario);
            }

            // Assert
            using var lectura = NuevoContexto();
            var guardado = lectura.Usuarios.AsNoTracking().Single();
            Assert.True(guardado.PinBloqueado(ahora));
        }

        [Fact]
        public void Usuarios_TwoWithTheSameUsername_AreRejectedByTheUniqueIndex()
        {
            // Arrange
            using var context = NuevoContexto();
            context.Usuarios.Add(Usuario.Crear("maria", "María", RolUsuario.Oficina, "hash"));
            context.SaveChanges();
            context.Usuarios.Add(Usuario.Crear("maria", "Otra", RolUsuario.Oficina, "hash"));

            // Act & Assert
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        [Fact]
        public async Task GetActivoPorPinHashAsync_ActiveOperario_IsFound()
        {
            // Arrange
            using (var escritura = NuevoContexto())
            {
                escritura.Operarios.Add(Operario.Crear("Juan", "hash-1234"));
                escritura.SaveChanges();
            }
            using var context = NuevoContexto();

            // Act
            var operario = await new OperarioRepository(context).GetActivoPorPinHashAsync("hash-1234");

            // Assert
            Assert.NotNull(operario);
            Assert.Equal("Juan", operario.Nombre);
        }

        [Fact]
        public async Task GetActivoPorPinHashAsync_DeactivatedOperario_IsNotFound()
        {
            // Arrange
            using (var escritura = NuevoContexto())
            {
                var operario = Operario.Crear("Juan", "hash-1234");
                operario.Desactivar();
                escritura.Operarios.Add(operario);
                escritura.SaveChanges();
            }
            using var context = NuevoContexto();

            // Act
            var encontrado = await new OperarioRepository(context).GetActivoPorPinHashAsync("hash-1234");

            // Assert
            Assert.Null(encontrado);
        }

        [Fact]
        public void Operarios_TwoActiveWithTheSamePin_AreRejectedByTheFilteredUniqueIndex()
        {
            // Arrange
            using var context = NuevoContexto();
            context.Operarios.Add(Operario.Crear("Juan", "hash-1234"));
            context.SaveChanges();
            context.Operarios.Add(Operario.Crear("Pedro", "hash-1234"));

            // Act & Assert
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        [Fact]
        public void Operarios_ADeactivatedOperariosPin_CanBeUsedAgain()
        {
            // Arrange
            using var context = NuevoContexto();
            var anterior = Operario.Crear("Juan", "hash-1234");
            anterior.Desactivar();
            context.Operarios.Add(anterior);
            context.SaveChanges();

            // Act
            context.Operarios.Add(Operario.Crear("Pedro", "hash-1234"));
            context.SaveChanges();

            // Assert
            Assert.Equal(2, context.Operarios.Count());
        }

        [Fact]
        public void Recepciones_AuditData_IsPersistedAndAReferencedUserCannotBeDeleted()
        {
            // Arrange
            var usuario = Usuario.Crear("recepcion", "Celular", RolUsuario.Recepcion, "hash");
            var operario = Operario.Crear("Juan", "hash-1234");
            var proveedor = Proveedor.Crear("Tambo A", usuario.Id);
            var registradoEn = new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);
            using (var escritura = NuevoContexto())
            {
                escritura.Usuarios.Add(usuario);
                escritura.Operarios.Add(operario);
                escritura.Proveedores.Add(proveedor);
                escritura.Recepciones.Add(RecepcionLeche.Registrar(proveedor.Id, ParametrosCalidad.Create(null, 17m, 5m, 1000m),
                    new DateTime(2026, 10, 20, 11, 0, 0), new DateTime(2026, 10, 20, 12, 0, 0), 1, null, usuario.Id, operario.Id, registradoEn, 1));
                escritura.SaveChanges();
            }

            // Act
            using var lectura = NuevoContexto();
            var recepcion = lectura.Recepciones.AsNoTracking().Single();

            // Assert
            Assert.Equal(usuario.Id, recepcion.RegistradoPorUsuarioId);
            Assert.Equal(operario.Id, recepcion.OperarioId);
            Assert.Equal(registradoEn, recepcion.RegistradoEn);
            Assert.Equal(usuario.Id, lectura.Proveedores.AsNoTracking().Single().CreadoPorUsuarioId);
            Assert.Throws<SqliteException>(() => lectura.Usuarios.Where(u => u.Id == usuario.Id).ExecuteDelete());
        }

        [Fact]
        public void PasswordHasher_Hash_VerifiesTheRightPasswordOnly()
        {
            // Arrange
            var hasher = new PasswordHasherPbkdf2();

            // Act
            var hash = hasher.Hash("una-contraseña-larga");

            // Assert
            Assert.NotEqual("una-contraseña-larga", hash);
            Assert.True(hasher.Verify(hash, "una-contraseña-larga"));
            Assert.False(hasher.Verify(hash, "otra"));
        }

        [Fact]
        public void PasswordHasher_SamePasswordTwice_GivesDifferentHashes()
        {
            // Arrange
            var hasher = new PasswordHasherPbkdf2();

            // Act & Assert: random salt
            Assert.NotEqual(hasher.Hash("una-contraseña-larga"), hasher.Hash("una-contraseña-larga"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("no-es-un-hash")]
        [InlineData("hash")]
        public void PasswordHasher_StoredValueThatIsNotAHash_NeverVerifies(string guardado)
        {
            // Arrange
            var hasher = new PasswordHasherPbkdf2();

            // Act & Assert
            Assert.False(hasher.Verify(guardado, "una-contraseña-larga"));
        }

        private static PinHasherHmac NuevoHasherDePin(string secreto) =>
            new(Options.Create(new PinSettings { Secret = secreto }));

        [Fact]
        public void PinHasher_SamePinAndSecret_GivesTheSameSixtyFourHexCharacters()
        {
            // Arrange
            var hasher = NuevoHasherDePin(new string('a', 32));

            // Act
            var primero = hasher.Hash("1234");
            var segundo = hasher.Hash("1234");

            // Assert
            Assert.Equal(primero, segundo);
            Assert.Equal(64, primero.Length);
            Assert.Matches("^[0-9a-f]{64}$", primero);
            Assert.NotEqual(primero, hasher.Hash("1235"));
        }

        [Fact]
        public void PinHasher_DifferentSecret_GivesADifferentHash()
        {
            // Arrange
            var uno = NuevoHasherDePin(new string('a', 32));
            var otro = NuevoHasherDePin(new string('b', 32));

            // Act & Assert: rotating the secret invalidates every stored PIN
            Assert.NotEqual(uno.Hash("1234"), otro.Hash("1234"));
        }
    }
}
