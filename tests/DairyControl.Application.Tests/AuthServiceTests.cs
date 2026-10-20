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
using System.IdentityModel.Tokens.Jwt;

namespace DairyControl.Application.Tests
{
    public class AuthServiceTests
    {
        private static readonly DateTimeOffset Ahora = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

        private sealed class Escenario
        {
            public Mock<IUsuarioRepository> Usuarios { get; } = new();
            public Mock<IHasherDePassword> Hasher { get; } = new();
            public Mock<IUsuarioActual> Actual { get; } = new();
            public Usuario Usuario { get; }

            public Escenario(RolUsuario rol = RolUsuario.Oficina, bool debeCambiar = false)
            {
                Usuario = Usuario.Crear("maria", "María Pérez", rol, "hash-guardado", debeCambiar);
                Usuarios.Setup(u => u.GetByUsernameAsync("maria")).ReturnsAsync(Usuario);
                Usuarios.Setup(u => u.GetByIdAsync(Usuario.Id)).ReturnsAsync(Usuario);
                Usuarios.AplicarModificaciones(Usuario);
                Hasher.Setup(h => h.Verify("hash-guardado", "clave-correcta")).Returns(true);
                Hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => "hash-de-" + p);
                Actual.Setup(a => a.Id).Returns(Usuario.Id);
                Actual.Setup(a => a.Rol).Returns(rol);
            }

            public AuthService Servicio() => new(
                Options.Create(new JwtSettings
                {
                    Issuer = "dairycontrol",
                    Audience = "dairycontrol-client",
                    SigningKey = "una-clave-de-prueba-de-al-menos-32-caracteres",
                    RecepcionDias = 7,
                    PersonalHoras = 12,
                }),
                Usuarios.Object, Hasher.Object, Actual.Object, new RelojFijo(Ahora));
        }

        private static JwtSecurityToken Leer(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

        [Fact]
        public async Task LoginAsync_ValidCredentials_TokenCarriesIdRoleStampAndName()
        {
            // Arrange
            var escenario = new Escenario(RolUsuario.Oficina);

            // Act
            var result = await escenario.Servicio().LoginAsync(new LoginDto { Username = " maria ", Password = "clave-correcta" });

            // Assert
            Assert.NotNull(result);
            var jwt = Leer(result.Token);
            Assert.Equal(escenario.Usuario.Id.ToString(), jwt.Subject);
            Assert.Equal("Oficina", jwt.Claims.Single(c => c.Type == "role").Value);
            Assert.Equal(escenario.Usuario.SecurityStamp.ToString(), jwt.Claims.Single(c => c.Type == "stamp").Value);
            Assert.Equal("María Pérez", jwt.Claims.Single(c => c.Type == "name").Value);
            Assert.False(result.DebeCambiarPassword);
        }

        [Theory]
        [InlineData(RolUsuario.Admin, 12 * 60)]
        [InlineData(RolUsuario.Oficina, 12 * 60)]
        [InlineData(RolUsuario.Consulta, 12 * 60)]
        [InlineData(RolUsuario.Recepcion, 7 * 24 * 60)]
        public async Task LoginAsync_ValidCredentials_TokenLifetimeDependsOnTheRole(RolUsuario rol, int minutos)
        {
            // Arrange
            var escenario = new Escenario(rol);

            // Act
            var result = await escenario.Servicio().LoginAsync(new LoginDto { Username = "maria", Password = "clave-correcta" });

            // Assert
            Assert.NotNull(result);
            Assert.Equal(Ahora.UtcDateTime.AddMinutes(minutos), Leer(result.Token).ValidTo);
        }

        [Fact]
        public async Task LoginAsync_UserMustChangePassword_FlagsItInTheResultAndTheToken()
        {
            // Arrange
            var escenario = new Escenario(debeCambiar: true);

            // Act
            var result = await escenario.Servicio().LoginAsync(new LoginDto { Username = "maria", Password = "clave-correcta" });

            // Assert
            Assert.NotNull(result);
            Assert.True(result.DebeCambiarPassword);
            Assert.Equal("1", Leer(result.Token).Claims.Single(c => c.Type == "dcp").Value);
        }

        [Fact]
        public async Task LoginAsync_WrongPassword_ReturnsNullAndCountsTheFailure()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var result = await escenario.Servicio().LoginAsync(new LoginDto { Username = "maria", Password = "otra" });

            // Assert
            Assert.Null(result);
            Assert.Equal(1, escenario.Usuario.FallosLogin);
            escenario.Usuarios.Verify(u => u.ModificarAsync(escenario.Usuario.Id, It.IsAny<Func<Usuario, bool>>()), Times.Once);
        }

        [Fact]
        public async Task LoginAsync_UnknownUser_ReturnsNullAndStillRunsAHashVerification()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Usuarios.Setup(u => u.GetByUsernameAsync("nadie")).ReturnsAsync((Usuario?)null);

            // Act
            var result = await escenario.Servicio().LoginAsync(new LoginDto { Username = "nadie", Password = "clave" });

            // Assert
            Assert.Null(result);
            escenario.Hasher.Verify(h => h.Verify(It.IsAny<string>(), "clave"), Times.Once); // same work as for a known user
            escenario.Usuarios.Verify(u => u.ModificarAsync(It.IsAny<Guid>(), It.IsAny<Func<Usuario, bool>>()), Times.Never);
        }

        [Fact]
        public async Task LoginAsync_InactiveUser_ReturnsNullEvenWithTheRightPassword()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Usuario.Desactivar(DairyControl.Domain.ValueObjects.MotivoDeCambio.Create("Baja de prueba"), Guid.NewGuid(), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

            // Act
            var result = await escenario.Servicio().LoginAsync(new LoginDto { Username = "maria", Password = "clave-correcta" });

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task LoginAsync_FiveWrongPasswords_LocksTheUserEvenForTheRightPassword()
        {
            // Arrange
            var escenario = new Escenario();
            var servicio = escenario.Servicio();
            for (var i = 0; i < Usuario.MaxFallosLogin; i++)
            {
                await servicio.LoginAsync(new LoginDto { Username = "maria", Password = "mala" });
            }

            // Act
            var result = await servicio.LoginAsync(new LoginDto { Username = "maria", Password = "clave-correcta" });

            // Assert
            Assert.Null(result); // the same answer as a wrong password: nothing tells the account is locked
            Assert.True(escenario.Usuario.LoginBloqueado(Ahora.UtcDateTime));
        }

        [Fact]
        public async Task LoginAsync_RightPasswordAfterSomeFailures_ResetsTheCount()
        {
            // Arrange
            var escenario = new Escenario();
            var servicio = escenario.Servicio();
            await servicio.LoginAsync(new LoginDto { Username = "maria", Password = "mala" });
            await servicio.LoginAsync(new LoginDto { Username = "maria", Password = "mala" });

            // Act
            var result = await servicio.LoginAsync(new LoginDto { Username = "maria", Password = "clave-correcta" });

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0, escenario.Usuario.FallosLogin);
        }

        [Fact]
        public async Task RenovarAsync_ActiveUser_ReturnsAFreshToken()
        {
            // Arrange
            var escenario = new Escenario(RolUsuario.Recepcion);

            // Act
            var result = await escenario.Servicio().RenovarAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(Ahora.UtcDateTime.AddDays(7), Leer(result.Token).ValidTo);
        }

        [Fact]
        public async Task RenovarAsync_InactiveUser_ReturnsNull()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Usuario.Desactivar(DairyControl.Domain.ValueObjects.MotivoDeCambio.Create("Baja de prueba"), Guid.NewGuid(), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

            // Act
            var result = await escenario.Servicio().RenovarAsync();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ValidarSesionAsync_CurrentStamp_IsValid()
        {
            // Arrange
            var escenario = new Escenario(debeCambiar: true);

            // Act
            var result = await escenario.Servicio().ValidarSesionAsync(escenario.Usuario.Id, escenario.Usuario.SecurityStamp);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.DebeCambiarPassword);
        }

        [Fact]
        public async Task ValidarSesionAsync_StampChangedAfterAPasswordChange_IsInvalid()
        {
            // Arrange
            var escenario = new Escenario();
            var selloViejo = escenario.Usuario.SecurityStamp;
            escenario.Usuario.CambiarPassword("otro-hash");

            // Act
            var result = await escenario.Servicio().ValidarSesionAsync(escenario.Usuario.Id, selloViejo);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ValidarSesionAsync_DeactivatedUser_IsInvalid()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Usuario.Desactivar(DairyControl.Domain.ValueObjects.MotivoDeCambio.Create("Baja de prueba"), Guid.NewGuid(), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

            // Act
            var result = await escenario.Servicio().ValidarSesionAsync(escenario.Usuario.Id, escenario.Usuario.SecurityStamp);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ValidarSesionAsync_UnknownUser_IsInvalid()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Usuarios.Setup(u => u.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Usuario?)null);

            // Act
            var result = await escenario.Servicio().ValidarSesionAsync(Guid.NewGuid(), Guid.NewGuid());

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task CambiarPasswordAsync_ValidChange_StoresTheNewHashClearsTheObligationAndReturnsANewToken()
        {
            // Arrange
            var escenario = new Escenario(debeCambiar: true);
            var selloViejo = escenario.Usuario.SecurityStamp;

            // Act
            var result = await escenario.Servicio().CambiarPasswordAsync(new CambiarPasswordDto { PasswordActual = "clave-correcta", PasswordNueva = "una-clave-nueva" });

            // Assert
            Assert.NotNull(result);
            Assert.False(result.DebeCambiarPassword);
            Assert.Equal("hash-de-una-clave-nueva", escenario.Usuario.PasswordHash);
            Assert.False(escenario.Usuario.DebeCambiarPassword);
            Assert.NotEqual(selloViejo, escenario.Usuario.SecurityStamp);
            Assert.Equal(escenario.Usuario.SecurityStamp.ToString(), Leer(result.Token).Claims.Single(c => c.Type == "stamp").Value);
            escenario.Usuarios.Verify(u => u.UpdateAsync(escenario.Usuario), Times.Once);
        }

        [Fact]
        public async Task CambiarPasswordAsync_WrongCurrentPassword_ThrowsOnPasswordActualAndCountsTheFailure()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() =>
                escenario.Servicio().CambiarPasswordAsync(new CambiarPasswordDto { PasswordActual = "mala", PasswordNueva = "una-clave-nueva" }));

            // Assert
            Assert.Equal("passwordActual", ex.Campo);
            Assert.Equal(1, escenario.Usuario.FallosLogin);
            Assert.Equal("hash-guardado", escenario.Usuario.PasswordHash);
        }

        [Fact]
        public async Task CambiarPasswordAsync_NewPasswordTooShort_ThrowsOnPasswordNueva()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() =>
                escenario.Servicio().CambiarPasswordAsync(new CambiarPasswordDto { PasswordActual = "clave-correcta", PasswordNueva = "corta" }));

            // Assert
            Assert.Equal("passwordNueva", ex.Campo);
        }

        [Fact]
        public async Task CambiarPasswordAsync_SameAsTheCurrentOne_ThrowsOnPasswordNueva()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() =>
                escenario.Servicio().CambiarPasswordAsync(new CambiarPasswordDto { PasswordActual = "clave-correcta", PasswordNueva = "clave-correcta" }));

            // Assert
            Assert.Equal("passwordNueva", ex.Campo);
        }

        [Fact]
        public async Task CambiarPasswordAsync_LockedUser_ThrowsDemasiadosIntentos()
        {
            // Arrange
            var escenario = new Escenario();
            for (var i = 0; i <= Usuario.MaxFallosLogin; i++)
            {
                escenario.Usuario.ReservarIntentoLogin(Ahora.UtcDateTime);
            }

            // Act & Assert
            await Assert.ThrowsAsync<DemasiadosIntentosException>(() =>
                escenario.Servicio().CambiarPasswordAsync(new CambiarPasswordDto { PasswordActual = "clave-correcta", PasswordNueva = "una-clave-nueva" }));
        }
    }
}
