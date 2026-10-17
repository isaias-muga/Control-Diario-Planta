using DairyControl.Application.Seguridad;
using DairyControl.Application.Services;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using Moq;

namespace DairyControl.Application.Tests
{
    public class VerificacionPinServiceTests
    {
        private static readonly DateTimeOffset Ahora = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

        private sealed class Escenario
        {
            public Mock<IUsuarioRepository> Usuarios { get; } = new();
            public Mock<IOperarioRepository> Operarios { get; } = new();
            public Mock<IHasherDePin> Hasher { get; } = new();
            public Mock<IUsuarioActual> Actual { get; } = new();
            public Usuario Usuario { get; } = Usuario.Crear("recepcion", "Celular recepción", RolUsuario.Recepcion, "hash");
            public Operario Operario { get; } = Operario.Crear("Juan Pérez", "hash-1234");

            public Escenario(RolUsuario rol = RolUsuario.Recepcion)
            {
                Actual.Setup(a => a.Id).Returns(Usuario.Id);
                Actual.Setup(a => a.Rol).Returns(rol);
                Usuarios.Setup(u => u.GetByIdAsync(Usuario.Id)).ReturnsAsync(Usuario);
                Hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => "hash-" + p);
                Operarios.Setup(o => o.GetActivoPorPinHashAsync("hash-1234")).ReturnsAsync(Operario);
            }

            public VerificacionPinService Servicio(TimeProvider? reloj = null) =>
                new(Usuarios.Object, Operarios.Object, Hasher.Object, Actual.Object, reloj ?? new RelojFijo(Ahora));
        }

        [Fact]
        public async Task ResolverAsync_ValidPin_ReturnsTheOperario()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var operario = await escenario.Servicio().ResolverAsync("1234");

            // Assert
            Assert.Same(escenario.Operario, operario);
        }

        [Theory]
        [InlineData(RolUsuario.Admin)]
        [InlineData(RolUsuario.Oficina)]
        public async Task ResolverAsync_RoleThatNeedsNoPin_ReturnsNullWithoutLookingAnything(RolUsuario rol)
        {
            // Arrange
            var escenario = new Escenario(rol);

            // Act
            var operario = await escenario.Servicio().ResolverAsync(null);

            // Assert
            Assert.Null(operario);
            escenario.Operarios.Verify(o => o.GetActivoPorPinHashAsync(It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task ResolverAsync_MissingPin_ThrowsReglaDeNegocioExceptionOnPin(string? pin)
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().ResolverAsync(pin));

            // Assert
            Assert.Equal("pin", ex.Campo);
        }

        [Theory]
        [InlineData("12")]
        [InlineData("abcd")]
        [InlineData("12345")]
        public async Task ResolverAsync_MalformedPin_ThrowsOnPinWithoutCountingAFailure(string pin)
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().ResolverAsync(pin));

            // Assert
            Assert.Equal("pin", ex.Campo);
            Assert.Equal(0, escenario.Usuario.FallosPin);
            escenario.Usuarios.Verify(u => u.UpdateAsync(It.IsAny<Usuario>()), Times.Never);
        }

        [Fact]
        public async Task ResolverAsync_WrongPin_ThrowsReglaDeNegocioExceptionOnPinAndPersistsTheFailure()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().ResolverAsync("9999"));

            // Assert
            Assert.Equal("pin", ex.Campo);
            Assert.Equal(1, escenario.Usuario.FallosPin);
            escenario.Usuarios.Verify(u => u.UpdateAsync(escenario.Usuario), Times.Once);
        }

        [Fact]
        public async Task ResolverAsync_PinOfADeactivatedOperario_IsWrong()
        {
            // Arrange: the repository only returns active operarios
            var escenario = new Escenario();
            escenario.Operarios.Setup(o => o.GetActivoPorPinHashAsync("hash-1234")).ReturnsAsync((Operario?)null);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().ResolverAsync("1234"));

            // Assert
            Assert.Equal("pin", ex.Campo);
        }

        [Fact]
        public async Task ResolverAsync_FifthWrongPin_ThrowsDemasiadosIntentosAndSavedTheBlock()
        {
            // Arrange
            var escenario = new Escenario();
            var servicio = escenario.Servicio();
            for (var i = 0; i < Usuario.MaxFallosPin - 1; i++)
            {
                await Assert.ThrowsAsync<ReglaDeNegocioException>(() => servicio.ResolverAsync("9999"));
            }

            // Act
            await Assert.ThrowsAsync<DemasiadosIntentosException>(() => servicio.ResolverAsync("9999"));

            // Assert
            Assert.True(escenario.Usuario.PinBloqueado(Ahora.UtcDateTime));
            escenario.Usuarios.Verify(u => u.UpdateAsync(escenario.Usuario), Times.Exactly(Usuario.MaxFallosPin));
        }

        [Fact]
        public async Task ResolverAsync_WhileBlocked_RefusesEvenTheRightPinWithoutCheckingIt()
        {
            // Arrange
            var escenario = new Escenario();
            for (var i = 0; i < Usuario.MaxFallosPin; i++)
            {
                escenario.Usuario.RegistrarPinIncorrecto(Ahora.UtcDateTime);
            }

            // Act
            await Assert.ThrowsAsync<DemasiadosIntentosException>(() => escenario.Servicio().ResolverAsync("1234"));

            // Assert
            escenario.Operarios.Verify(o => o.GetActivoPorPinHashAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ResolverAsync_AfterTheBlockExpires_AcceptsTheRightPinAgain()
        {
            // Arrange
            var escenario = new Escenario();
            for (var i = 0; i < Usuario.MaxFallosPin; i++)
            {
                escenario.Usuario.RegistrarPinIncorrecto(Ahora.UtcDateTime);
            }
            var despues = new RelojFijo(Ahora.AddMinutes(6));

            // Act
            var operario = await escenario.Servicio(despues).ResolverAsync("1234");

            // Assert
            Assert.Same(escenario.Operario, operario);
            Assert.False(escenario.Usuario.PinBloqueado(Ahora.UtcDateTime.AddMinutes(6)));
            Assert.Null(escenario.Usuario.PinBloqueadoHastaUtc);
        }

        [Fact]
        public async Task ResolverAsync_RightPinAfterSomeFailures_ResetsTheCount()
        {
            // Arrange
            var escenario = new Escenario();
            var servicio = escenario.Servicio();
            await Assert.ThrowsAsync<ReglaDeNegocioException>(() => servicio.ResolverAsync("9999"));

            // Act
            await servicio.ResolverAsync("1234");

            // Assert
            Assert.Equal(0, escenario.Usuario.FallosPin);
        }

        [Fact]
        public async Task ResolverAsync_RightPinWithNoFailures_DoesNotWriteAnything()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            await escenario.Servicio().ResolverAsync("1234");

            // Assert
            escenario.Usuarios.Verify(u => u.UpdateAsync(It.IsAny<Usuario>()), Times.Never);
        }
    }
}
