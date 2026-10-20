using DairyControl.Application.DTOs;
using DairyControl.Application.Queries;
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
        private static readonly DateTimeOffset Ahora = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

        private sealed class Escenario
        {
            public Mock<IUsuarioRepository> Usuarios { get; } = new();
            public Mock<IHasherDePassword> Hasher { get; } = new();
            public Mock<IUsuarioActual> Actual { get; } = new();
            public Mock<IHistorialQueries> Historial { get; } = new();
            public Guid ActualId { get; } = Guid.NewGuid();

            public Escenario()
            {
                Hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => "hash-de-" + p);
                Actual.Setup(a => a.Id).Returns(ActualId);
            }

            public UsuarioAppService Servicio(string adminPassword = "contraseña-del-despliegue", AdminUserSettings? ajustes = null) =>
                new(Usuarios.Object, Hasher.Object, Actual.Object,
                    Options.Create(ajustes ?? new AdminUserSettings { Username = "admin", Password = adminPassword }),
                    Historial.Object, new RelojFijo(Ahora));

            public Usuario Existente(string username = "maria", RolUsuario rol = RolUsuario.Oficina)
            {
                var usuario = Usuario.Crear(username, "María", rol, "hash", debeCambiarPassword: false);
                Usuarios.Setup(u => u.GetByIdAsync(usuario.Id)).ReturnsAsync(usuario);
                return usuario;
            }
        }

        private static CrearUsuarioDto Dto(string rol = "Oficina") =>
            new() { Username = "maria", Nombre = "María Pérez", Rol = rol, Password = "temporal-123" };

        private static MotivoCambioDto Motivo(string texto = "Pidió la baja") => new() { Motivo = texto };

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
            Assert.False(result.Protegido);
            escenario.Usuarios.Verify(u => u.AddAsync(
                It.Is<Usuario>(x => x.PasswordHash == "hash-de-temporal-123" && x.DebeCambiarPassword && !x.Protegido),
                It.Is<CambioRegistrado>(c => c.Entidad == CambioRegistrado.EntidadUsuario && c.Campo == CambioRegistrado.Campos.Rol
                    && c.UsuarioId == escenario.ActualId && c.EnUtc == Ahora.UtcDateTime)), Times.Once);
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
            escenario.Usuarios.Verify(u => u.AddAsync(It.IsAny<Usuario>(), It.IsAny<CambioRegistrado>()), Times.Never);
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
            escenario.Usuarios.Verify(u => u.AddAsync(It.IsAny<Usuario>(), It.IsAny<CambioRegistrado>()), Times.Never);
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
            escenario.Usuarios.Verify(u => u.AddAsync(It.IsAny<Usuario>(), It.IsAny<CambioRegistrado>()), Times.Never);
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
        public void InputDtos_NeverCarryTheProtectedFlag()
        {
            // Arrange
            var dtos = new[] { typeof(CrearUsuarioDto), typeof(CambiarRolDto), typeof(MotivoCambioDto), typeof(RestablecerPasswordDto) };

            // Act & Assert: only configuration sets the flag; no request can
            Assert.All(dtos, tipo => Assert.DoesNotContain(tipo.GetProperties(), p => p.Name.Contains("Proteg")));
        }

        [Fact]
        public async Task DesactivarAsync_OtherUser_DeactivatesAndSavesTheEntryWithTheReason()
        {
            // Arrange
            var escenario = new Escenario();
            var usuario = escenario.Existente();

            // Act
            var result = await escenario.Servicio().DesactivarAsync(usuario.Id, Motivo("Dejó la empresa"));

            // Assert
            Assert.True(result);
            Assert.False(usuario.Activo);
            escenario.Usuarios.Verify(u => u.GuardarCambioAsync(usuario,
                It.Is<CambioRegistrado>(c => c.Campo == CambioRegistrado.Campos.Activo && c.Motivo == "Dejó la empresa"
                    && c.UsuarioId == escenario.ActualId && c.EnUtc == Ahora.UtcDateTime && c.EntidadId == usuario.Id),
                false), Times.Once);
        }

        [Fact]
        public async Task DesactivarAsync_AnotherActiveAdmin_AsksTheRepositoryToKeepOneAdmin()
        {
            // Arrange
            var escenario = new Escenario();
            var otroAdmin = escenario.Existente("otro", RolUsuario.Admin);

            // Act
            await escenario.Servicio().DesactivarAsync(otroAdmin.Id, Motivo());

            // Assert
            escenario.Usuarios.Verify(u => u.GuardarCambioAsync(otroAdmin, It.IsAny<CambioRegistrado>(), true), Times.Once);
        }

        [Fact]
        public async Task DesactivarAsync_TheCurrentUser_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var escenario = new Escenario();
            var yo = Usuario.Crear("admin", "Administrador", RolUsuario.Admin, "hash", false);
            escenario.Usuarios.Setup(u => u.GetByIdAsync(escenario.ActualId)).ReturnsAsync(yo);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().DesactivarAsync(escenario.ActualId, Motivo()));

            // Assert
            Assert.Equal("id", ex.Campo);
            Assert.True(yo.Activo);
            escenario.Usuarios.Verify(u => u.GuardarCambioAsync(It.IsAny<Usuario>(), It.IsAny<CambioRegistrado>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task DesactivarAsync_ProtectedAccount_ThrowsCuentaProtegidaAndSavesNothing()
        {
            // Arrange
            var escenario = new Escenario();
            var dueno = escenario.Existente("dueno", RolUsuario.Admin);
            dueno.MarcarProtegido(true);

            // Act
            await Assert.ThrowsAsync<CuentaProtegidaException>(() => escenario.Servicio().DesactivarAsync(dueno.Id, Motivo()));

            // Assert
            Assert.True(dueno.Activo);
            escenario.Usuarios.Verify(u => u.GuardarCambioAsync(It.IsAny<Usuario>(), It.IsAny<CambioRegistrado>(), It.IsAny<bool>()), Times.Never);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("abcd")]
        public async Task DesactivarAsync_MissingOrShortReason_ThrowsOnMotivoAndSavesNothing(string? motivo)
        {
            // Arrange
            var escenario = new Escenario();
            var usuario = escenario.Existente();
            var dto = new MotivoCambioDto { Motivo = motivo };

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().DesactivarAsync(usuario.Id, dto));

            // Assert
            Assert.Equal("motivo", ex.Campo);
            Assert.True(usuario.Activo);
            escenario.Usuarios.Verify(u => u.GuardarCambioAsync(It.IsAny<Usuario>(), It.IsAny<CambioRegistrado>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task DesactivarAsync_UnknownUser_ReturnsFalse()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var result = await escenario.Servicio().DesactivarAsync(Guid.NewGuid(), Motivo());

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ReactivarAsync_InactiveUser_ReactivatesAndMustChangeThePassword()
        {
            // Arrange
            var escenario = new Escenario();
            var usuario = escenario.Existente();
            usuario.Desactivar(MotivoDeCambioParaPruebas(), Guid.NewGuid(), Ahora.UtcDateTime);

            // Act
            var result = await escenario.Servicio().ReactivarAsync(usuario.Id, Motivo("Volvió a trabajar"));

            // Assert
            Assert.True(result);
            Assert.True(usuario.Activo);
            Assert.True(usuario.DebeCambiarPassword);
            escenario.Usuarios.Verify(u => u.GuardarCambioAsync(usuario,
                It.Is<CambioRegistrado>(c => c.Campo == CambioRegistrado.Campos.Activo && c.ValorNuevo == "Activo" && c.Motivo == "Volvió a trabajar"),
                false), Times.Once);
        }

        [Fact]
        public async Task ReactivarAsync_UnknownUser_ReturnsFalse()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var result = await escenario.Servicio().ReactivarAsync(Guid.NewGuid(), Motivo());

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ReactivarAsync_AlreadyActive_ThrowsAndSavesNothing()
        {
            // Arrange
            var escenario = new Escenario();
            var usuario = escenario.Existente();

            // Act
            await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().ReactivarAsync(usuario.Id, Motivo()));

            // Assert
            escenario.Usuarios.Verify(u => u.GuardarCambioAsync(It.IsAny<Usuario>(), It.IsAny<CambioRegistrado>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task CambiarRolAsync_ValidRol_ChangesItAndSavesTheEntry()
        {
            // Arrange
            var escenario = new Escenario();
            var usuario = escenario.Existente(rol: RolUsuario.Oficina);

            // Act
            var result = await escenario.Servicio().CambiarRolAsync(usuario.Id, new CambiarRolDto { Rol = "consulta", Motivo = "Pasa a solo lectura" });

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Consulta", result.Rol);
            Assert.Equal(RolUsuario.Consulta, usuario.Rol);
            escenario.Usuarios.Verify(u => u.GuardarCambioAsync(usuario,
                It.Is<CambioRegistrado>(c => c.Campo == CambioRegistrado.Campos.Rol && c.ValorAnterior == "Oficina" && c.ValorNuevo == "Consulta"),
                false), Times.Once);
        }

        [Fact]
        public async Task CambiarRolAsync_DemotingAnActiveAdmin_AsksTheRepositoryToKeepOneAdmin()
        {
            // Arrange
            var escenario = new Escenario();
            var admin = escenario.Existente("otro", RolUsuario.Admin);

            // Act
            await escenario.Servicio().CambiarRolAsync(admin.Id, new CambiarRolDto { Rol = "Oficina", Motivo = "Ya no administra" });

            // Assert
            escenario.Usuarios.Verify(u => u.GuardarCambioAsync(admin, It.IsAny<CambioRegistrado>(), true), Times.Once);
        }

        [Fact]
        public async Task CambiarRolAsync_PromotingToAdmin_DoesNotAskForAnotherAdmin()
        {
            // Arrange
            var escenario = new Escenario();
            var usuario = escenario.Existente(rol: RolUsuario.Oficina);

            // Act
            await escenario.Servicio().CambiarRolAsync(usuario.Id, new CambiarRolDto { Rol = "Admin", Motivo = "Nuevo administrador" });

            // Assert
            escenario.Usuarios.Verify(u => u.GuardarCambioAsync(usuario, It.IsAny<CambioRegistrado>(), false), Times.Once);
        }

        [Fact]
        public async Task CambiarRolAsync_ProtectedAccount_ThrowsCuentaProtegidaAndKeepsTheRole()
        {
            // Arrange
            var escenario = new Escenario();
            var dueno = escenario.Existente("dueno", RolUsuario.Admin);
            dueno.MarcarProtegido(true);

            // Act
            await Assert.ThrowsAsync<CuentaProtegidaException>(() =>
                escenario.Servicio().CambiarRolAsync(dueno.Id, new CambiarRolDto { Rol = "Consulta", Motivo = "Probando" }));

            // Assert
            Assert.Equal(RolUsuario.Admin, dueno.Rol);
            escenario.Usuarios.Verify(u => u.GuardarCambioAsync(It.IsAny<Usuario>(), It.IsAny<CambioRegistrado>(), It.IsAny<bool>()), Times.Never);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("Superman")]
        [InlineData("3")]
        public async Task CambiarRolAsync_InvalidRol_ThrowsOnRol(string? rol)
        {
            // Arrange
            var escenario = new Escenario();
            var usuario = escenario.Existente();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() =>
                escenario.Servicio().CambiarRolAsync(usuario.Id, new CambiarRolDto { Rol = rol, Motivo = "Cambio de tareas" }));

            // Assert
            Assert.Equal("rol", ex.Campo);
        }

        [Fact]
        public async Task CambiarRolAsync_UnknownUser_ReturnsNull()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var result = await escenario.Servicio().CambiarRolAsync(Guid.NewGuid(), new CambiarRolDto { Rol = "Admin", Motivo = "Cambio de tareas" });

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task RestablecerPasswordAsync_ValidPassword_SetsATemporaryOneThatMustBeChanged()
        {
            // Arrange
            var escenario = new Escenario();
            var usuario = escenario.Existente();

            // Act
            var result = await escenario.Servicio().RestablecerPasswordAsync(usuario.Id, new RestablecerPasswordDto { Password = "nueva-temporal" });

            // Assert
            Assert.True(result);
            Assert.Equal("hash-de-nueva-temporal", usuario.PasswordHash);
            Assert.True(usuario.DebeCambiarPassword);
            escenario.Usuarios.Verify(u => u.GuardarCambioAsync(usuario,
                It.Is<CambioRegistrado>(c => c.Campo == CambioRegistrado.Campos.Password && c.UsuarioId == escenario.ActualId),
                false), Times.Once);
        }

        [Fact]
        public async Task RestablecerPasswordAsync_ShortPassword_ThrowsOnPassword()
        {
            // Arrange
            var escenario = new Escenario();
            var usuario = escenario.Existente();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() =>
                escenario.Servicio().RestablecerPasswordAsync(usuario.Id, new RestablecerPasswordDto { Password = "x" }));

            // Assert
            Assert.Equal("password", ex.Campo);
        }

        [Fact]
        public async Task RestablecerPasswordAsync_ProtectedAccountOfAnotherAdmin_ThrowsCuentaProtegidaAndKeepsThePassword()
        {
            // Arrange
            var escenario = new Escenario();
            var dueno = escenario.Existente("dueno", RolUsuario.Admin);
            dueno.MarcarProtegido(true);

            // Act
            await Assert.ThrowsAsync<CuentaProtegidaException>(() =>
                escenario.Servicio().RestablecerPasswordAsync(dueno.Id, new RestablecerPasswordDto { Password = "nueva-temporal" }));

            // Assert
            Assert.Equal("hash", dueno.PasswordHash);
            escenario.Usuarios.Verify(u => u.GuardarCambioAsync(It.IsAny<Usuario>(), It.IsAny<CambioRegistrado>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task RestablecerPasswordAsync_ProtectedAccountOwnPassword_IsAllowed()
        {
            // Arrange
            var escenario = new Escenario();
            var dueno = Usuario.Crear("dueno", "Dueño", RolUsuario.Admin, "hash", false);
            dueno.MarcarProtegido(true);
            escenario.Usuarios.Setup(u => u.GetByIdAsync(dueno.Id)).ReturnsAsync(dueno);
            // The current user is the protected one itself
            var propio = new Mock<IUsuarioActual>();
            propio.Setup(a => a.Id).Returns(dueno.Id);
            var servicio = new UsuarioAppService(escenario.Usuarios.Object, escenario.Hasher.Object, propio.Object,
                Options.Create(new AdminUserSettings { Username = "admin", Password = "x" }), escenario.Historial.Object, new RelojFijo(Ahora));

            // Act
            var result = await servicio.RestablecerPasswordAsync(dueno.Id, new RestablecerPasswordDto { Password = "nueva-temporal" });

            // Assert
            Assert.True(result);
            Assert.Equal("hash-de-nueva-temporal", dueno.PasswordHash);
        }

        [Fact]
        public async Task GetHistorialAsync_ReturnsWhatTheQueryReturnsForTheUsuarioEntity()
        {
            // Arrange
            var escenario = new Escenario();
            var id = Guid.NewGuid();
            IReadOnlyList<CorreccionDto> historial = [new CorreccionDto { Motivo = "Alta de usuario" }];
            escenario.Historial.Setup(h => h.GetAsync(CambioRegistrado.EntidadUsuario, id)).ReturnsAsync(historial);

            // Act
            var result = await escenario.Servicio().GetHistorialAsync(id);

            // Assert
            Assert.Same(historial, result);
        }

        [Fact]
        public async Task AsegurarAdminInicialAsync_NoUsersAndDefaultSettings_CreatesAProtectedAdminForcedToChangeThePassword()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Usuarios.Setup(u => u.AnyAsync()).ReturnsAsync(false);

            // Act
            var creado = await escenario.Servicio().AsegurarAdminInicialAsync();

            // Assert
            Assert.True(creado);
            escenario.Usuarios.Verify(u => u.AddAsync(
                It.Is<Usuario>(x => x.Username == "admin" && x.Rol == RolUsuario.Admin && x.DebeCambiarPassword && x.Protegido
                    && x.PasswordHash == "hash-de-contraseña-del-despliegue"),
                It.Is<CambioRegistrado>(c => c.Campo == CambioRegistrado.Campos.Rol && c.Motivo == "Alta inicial")), Times.Once);
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
            escenario.Usuarios.Verify(u => u.AddAsync(It.Is<Usuario>(x => x.DebeCambiarPassword == forzar), It.IsAny<CambioRegistrado>()), Times.Once);
        }

        [Fact]
        public void AdminUserSettings_Default_ForcesThePasswordChangeAndProtectsNobodyExtra()
        {
            // Arrange & Act
            var ajustes = new AdminUserSettings();

            // Assert
            Assert.True(ajustes.ForzarCambioPassword);
            Assert.Equal(string.Empty, ajustes.Protegidos);
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
            escenario.Usuarios.Verify(u => u.AddAsync(It.IsAny<Usuario>(), It.IsAny<CambioRegistrado>()), Times.Never);
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

        [Fact]
        public async Task SincronizarProtegidosAsync_BootstrapAndListedUsers_AreProtectedAndTheRestAreNot()
        {
            // Arrange
            var escenario = new Escenario();
            var admin = Usuario.Crear("admin", "Administrador", RolUsuario.Admin, "h");
            var dueno = Usuario.Crear("Dueno", "Dueño", RolUsuario.Consulta, "h");
            var otro = Usuario.Crear("otro", "Otro", RolUsuario.Admin, "h");
            var exProtegido = Usuario.Crear("viejo", "Viejo", RolUsuario.Admin, "h");
            exProtegido.MarcarProtegido(true);
            escenario.Usuarios.Setup(u => u.GetAllAsync()).ReturnsAsync(new List<Usuario> { admin, dueno, otro, exProtegido });
            escenario.Usuarios.AplicarModificaciones(admin);
            escenario.Usuarios.AplicarModificaciones(dueno);
            escenario.Usuarios.AplicarModificaciones(exProtegido);
            var ajustes = new AdminUserSettings { Username = "admin", Password = "x", Protegidos = " dueno , ,admin" };

            // Act
            var inexistentes = await escenario.Servicio(ajustes: ajustes).SincronizarProtegidosAsync();

            // Assert
            Assert.True(admin.Protegido);
            Assert.True(dueno.Protegido);
            Assert.False(otro.Protegido);
            Assert.False(exProtegido.Protegido);
            Assert.Empty(inexistentes);
            // Only the three that had to change are saved, each on top of its current state; "otro" is already right
            escenario.Usuarios.Verify(u => u.ModificarAsync(It.IsAny<Guid>(), It.IsAny<Func<Usuario, bool>>()), Times.Exactly(3));
            escenario.Usuarios.Verify(u => u.ModificarAsync(otro.Id, It.IsAny<Func<Usuario, bool>>()), Times.Never);
        }

        [Fact]
        public async Task SincronizarProtegidosAsync_NothingChanges_SavesNothing()
        {
            // Arrange
            var escenario = new Escenario();
            var admin = Usuario.Crear("admin", "Administrador", RolUsuario.Admin, "h");
            admin.MarcarProtegido(true);
            escenario.Usuarios.Setup(u => u.GetAllAsync()).ReturnsAsync(new List<Usuario> { admin });

            // Act
            await escenario.Servicio().SincronizarProtegidosAsync();

            // Assert
            escenario.Usuarios.Verify(u => u.ModificarAsync(It.IsAny<Guid>(), It.IsAny<Func<Usuario, bool>>()), Times.Never);
        }

        [Fact]
        public async Task SincronizarProtegidosAsync_ListedUsernameWithoutUser_IsReportedForTheLog()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Usuarios.Setup(u => u.GetAllAsync()).ReturnsAsync(new List<Usuario>());
            var ajustes = new AdminUserSettings { Username = "admin", Password = "x", Protegidos = "dueno" };

            // Act
            var inexistentes = await escenario.Servicio(ajustes: ajustes).SincronizarProtegidosAsync();

            // Assert
            Assert.Equal(new[] { "dueno", "admin" }.OrderBy(x => x), inexistentes.OrderBy(x => x));
        }

        private static DairyControl.Domain.ValueObjects.MotivoDeCambio MotivoDeCambioParaPruebas() =>
            DairyControl.Domain.ValueObjects.MotivoDeCambio.Create("Baja de prueba");
    }
}
