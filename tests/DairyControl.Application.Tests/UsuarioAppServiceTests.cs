using DairyControl.Application.DTOs;
using DairyControl.Application.Seguridad;
using DairyControl.Application.Services;
using DairyControl.Application.Settings;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using Microsoft.Extensions.Options;
using Moq;

namespace DairyControl.Application.Tests
{
    public class UsuarioAppServiceTests
    {
        private sealed class Escenario
        {
            public Mock<IUsuarioRepository> Usuarios { get; } = new();
            public Mock<IHasherDePassword> Hasher { get; } = new();
            public Mock<IUsuarioActual> Actual { get; } = new();
            public Guid ActualId { get; } = Guid.NewGuid();

            public Escenario()
            {
                Hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => "hash-de-" + p);
                Actual.Setup(a => a.Id).Returns(ActualId);
            }

            public UsuarioAppService Servicio(string adminPassword = "contraseña-del-despliegue", AdminUserSettings? ajustes = null) =>
                new(Usuarios.Object, Hasher.Object, Actual.Object,
                    Options.Create(ajustes ?? new AdminUserSettings { Username = "admin", Password = adminPassword }));
        }

        private static CrearUsuarioDto Dto(string rol = "Oficina") =>
            new() { Username = "maria", Nombre = "María Pérez", Rol = rol, Password = "temporal-123" };

        [Fact]
        public async Task CrearAsync_ValidDto_StoresAHashedPasswordAndMustChangeIt()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var result = await escenario.Servicio().CrearAsync(Dto("recepcion"));

            // Assert
            Assert.Equal("maria", result.Username);
            Assert.Equal("Recepcion", result.Rol);
            Assert.True(result.DebeCambiarPassword);
            escenario.Usuarios.Verify(u => u.AddAsync(It.Is<Usuario>(x => x.PasswordHash == "hash-de-temporal-123" && x.DebeCambiarPassword)), Times.Once);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Superman")]
        [InlineData("3")]
        [InlineData("0")]
        public async Task CrearAsync_InvalidRol_ThrowsReglaDeNegocioExceptionOnRol(string rol)
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().CrearAsync(Dto(rol)));

            // Assert
            Assert.Equal("rol", ex.Campo);
            escenario.Usuarios.Verify(u => u.AddAsync(It.IsAny<Usuario>()), Times.Never);
        }

        [Fact]
        public async Task CrearAsync_ShortPassword_ThrowsOnPasswordAndNeverSaves()
        {
            // Arrange
            var escenario = new Escenario();
            var dto = Dto();
            dto.Password = "corta";

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().CrearAsync(dto));

            // Assert
            Assert.Equal("password", ex.Campo);
            escenario.Usuarios.Verify(u => u.AddAsync(It.IsAny<Usuario>()), Times.Never);
        }

        [Fact]
        public async Task CrearAsync_UsernameAlreadyTaken_ThrowsConflictoDeUnicidad()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Usuarios.Setup(u => u.GetByUsernameAsync("maria"))
                .ReturnsAsync(Usuario.Crear("maria", "Otra María", RolUsuario.Oficina, "hash"));

            // Act & Assert
            await Assert.ThrowsAsync<ConflictoDeUnicidadException>(() => escenario.Servicio().CrearAsync(Dto()));
            escenario.Usuarios.Verify(u => u.AddAsync(It.IsAny<Usuario>()), Times.Never);
        }

        [Fact]
        public async Task GetAllAsync_ReturnsUsersByUsernameWithoutAnyHash()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Usuarios.Setup(u => u.GetAllAsync()).ReturnsAsync(new List<Usuario>
            {
                Usuario.Crear("zoe", "Zoe", RolUsuario.Oficina, "hash-z"),
                Usuario.Crear("ana", "Ana", RolUsuario.Admin, "hash-a"),
            });

            // Act
            var result = await escenario.Servicio().GetAllAsync();

            // Assert
            Assert.Equal(new[] { "ana", "zoe" }, result.Select(u => u.Username));
            Assert.DoesNotContain(typeof(UsuarioDto).GetProperties(), p => p.Name.Contains("Hash") || p.Name.Contains("Stamp"));
        }

        [Fact]
        public async Task DesactivarAsync_OtherUser_DeactivatesAndSaves()
        {
            // Arrange
            var escenario = new Escenario();
            var usuario = Usuario.Crear("maria", "María", RolUsuario.Oficina, "hash");
            escenario.Usuarios.Setup(u => u.GetByIdAsync(usuario.Id)).ReturnsAsync(usuario);

            // Act
            var result = await escenario.Servicio().DesactivarAsync(usuario.Id);

            // Assert
            Assert.True(result);
            Assert.False(usuario.Activo);
            escenario.Usuarios.Verify(u => u.UpdateAsync(usuario), Times.Once);
        }

        [Fact]
        public async Task DesactivarAsync_TheCurrentUser_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var escenario = new Escenario();
            var yo = Usuario.Crear("admin", "Administrador", RolUsuario.Admin, "hash", false);
            escenario.Usuarios.Setup(u => u.GetByIdAsync(escenario.ActualId)).ReturnsAsync(yo);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().DesactivarAsync(escenario.ActualId));

            // Assert
            Assert.Equal("id", ex.Campo);
            Assert.True(yo.Activo);
            escenario.Usuarios.Verify(u => u.UpdateAsync(It.IsAny<Usuario>()), Times.Never);
        }

        [Fact]
        public async Task DesactivarAsync_UnknownUser_ReturnsFalse()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var result = await escenario.Servicio().DesactivarAsync(Guid.NewGuid());

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task RestablecerPasswordAsync_ValidPassword_SetsATemporaryOneThatMustBeChanged()
        {
            // Arrange
            var escenario = new Escenario();
            var usuario = Usuario.Crear("maria", "María", RolUsuario.Oficina, "hash", debeCambiarPassword: false);
            escenario.Usuarios.Setup(u => u.GetByIdAsync(usuario.Id)).ReturnsAsync(usuario);

            // Act
            var result = await escenario.Servicio().RestablecerPasswordAsync(usuario.Id, new RestablecerPasswordDto { Password = "nueva-temporal" });

            // Assert
            Assert.True(result);
            Assert.Equal("hash-de-nueva-temporal", usuario.PasswordHash);
            Assert.True(usuario.DebeCambiarPassword);
            escenario.Usuarios.Verify(u => u.UpdateAsync(usuario), Times.Once);
        }

        [Fact]
        public async Task RestablecerPasswordAsync_ShortPassword_ThrowsOnPassword()
        {
            // Arrange
            var escenario = new Escenario();
            var usuario = Usuario.Crear("maria", "María", RolUsuario.Oficina, "hash");
            escenario.Usuarios.Setup(u => u.GetByIdAsync(usuario.Id)).ReturnsAsync(usuario);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() =>
                escenario.Servicio().RestablecerPasswordAsync(usuario.Id, new RestablecerPasswordDto { Password = "x" }));

            // Assert
            Assert.Equal("password", ex.Campo);
        }

        [Fact]
        public async Task AsegurarAdminInicialAsync_NoUsersAndDefaultSettings_CreatesTheAdminForcedToChangeThePassword()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Usuarios.Setup(u => u.AnyAsync()).ReturnsAsync(false);

            // Act
            var creado = await escenario.Servicio().AsegurarAdminInicialAsync();

            // Assert
            Assert.True(creado);
            escenario.Usuarios.Verify(u => u.AddAsync(It.Is<Usuario>(x =>
                x.Username == "admin" && x.Rol == RolUsuario.Admin && x.DebeCambiarPassword && x.PasswordHash == "hash-de-contraseña-del-despliegue")), Times.Once);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task AsegurarAdminInicialAsync_NoUsers_ForcesTheChangeOnlyWhenTheSettingSaysSo(bool forzar)
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Usuarios.Setup(u => u.AnyAsync()).ReturnsAsync(false);
            var ajustes = new AdminUserSettings { Username = "admin", Password = "contraseña-del-despliegue", ForzarCambioPassword = forzar };

            // Act
            await escenario.Servicio(ajustes: ajustes).AsegurarAdminInicialAsync();

            // Assert
            escenario.Usuarios.Verify(u => u.AddAsync(It.Is<Usuario>(x => x.DebeCambiarPassword == forzar)), Times.Once);
        }

        [Fact]
        public void AdminUserSettings_Default_ForcesThePasswordChange()
        {
            // Arrange & Act
            var ajustes = new AdminUserSettings();

            // Assert
            Assert.True(ajustes.ForzarCambioPassword);
        }

        [Fact]
        public async Task AsegurarAdminInicialAsync_UsersExist_DoesNothing()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Usuarios.Setup(u => u.AnyAsync()).ReturnsAsync(true);

            // Act
            var creado = await escenario.Servicio().AsegurarAdminInicialAsync();

            // Assert
            Assert.False(creado);
            escenario.Usuarios.Verify(u => u.AddAsync(It.IsAny<Usuario>()), Times.Never);
        }

        [Fact]
        public async Task AsegurarAdminInicialAsync_NoUsersAndNoSecret_FailsClearly()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Usuarios.Setup(u => u.AnyAsync()).ReturnsAsync(false);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => escenario.Servicio(adminPassword: "").AsegurarAdminInicialAsync());
        }
    }
}
