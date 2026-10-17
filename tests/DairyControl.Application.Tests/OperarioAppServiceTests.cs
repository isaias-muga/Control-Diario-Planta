using DairyControl.Application.DTOs;
using DairyControl.Application.Seguridad;
using DairyControl.Application.Services;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using Moq;

namespace DairyControl.Application.Tests
{
    public class OperarioAppServiceTests
    {
        private sealed class Escenario
        {
            public Mock<IOperarioRepository> Operarios { get; } = new();
            public Mock<IHasherDePin> Hasher { get; } = new();

            public Escenario() => Hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => "hash-" + p);

            public OperarioAppService Servicio() => new(Operarios.Object, Hasher.Object);
        }

        [Fact]
        public async Task CrearAsync_ValidPin_StoresOnlyTheHash()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var result = await escenario.Servicio().CrearAsync(new CrearOperarioDto { Nombre = "Juan Pérez", Pin = "1234" });

            // Assert
            Assert.Equal("Juan Pérez", result.Nombre);
            Assert.True(result.Activo);
            escenario.Operarios.Verify(o => o.AddAsync(It.Is<Operario>(x => x.PinHash == "hash-1234")), Times.Once);
            Assert.DoesNotContain(typeof(OperarioDto).GetProperties(), p => p.Name.Contains("Pin"));
        }

        [Theory]
        [InlineData("123")]
        [InlineData("abcd")]
        [InlineData("")]
        public async Task CrearAsync_InvalidPin_ThrowsOnPinAndNeverSaves(string pin)
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().CrearAsync(new CrearOperarioDto { Nombre = "Juan", Pin = pin }));

            // Assert
            Assert.Equal("pin", ex.Campo);
            escenario.Operarios.Verify(o => o.AddAsync(It.IsAny<Operario>()), Times.Never);
        }

        [Fact]
        public async Task CrearAsync_PinOfAnotherActiveOperario_ThrowsConflictoDeUnicidad()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Operarios.Setup(o => o.GetActivoPorPinHashAsync("hash-1234")).ReturnsAsync(Operario.Crear("Otro", "hash-1234"));

            // Act & Assert
            await Assert.ThrowsAsync<ConflictoDeUnicidadException>(() => escenario.Servicio().CrearAsync(new CrearOperarioDto { Nombre = "Juan", Pin = "1234" }));
            escenario.Operarios.Verify(o => o.AddAsync(It.IsAny<Operario>()), Times.Never);
        }

        [Fact]
        public async Task DesactivarAsync_ExistingOperario_DeactivatesAndSaves()
        {
            // Arrange
            var escenario = new Escenario();
            var operario = Operario.Crear("Juan", "hash-1234");
            escenario.Operarios.Setup(o => o.GetByIdAsync(operario.Id)).ReturnsAsync(operario);

            // Act
            var result = await escenario.Servicio().DesactivarAsync(operario.Id);

            // Assert
            Assert.True(result);
            Assert.False(operario.Activo);
            escenario.Operarios.Verify(o => o.UpdateAsync(operario), Times.Once);
        }

        [Fact]
        public async Task DesactivarAsync_UnknownOperario_ReturnsFalse()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var result = await escenario.Servicio().DesactivarAsync(Guid.NewGuid());

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task CambiarPinAsync_FreePin_ReplacesTheHash()
        {
            // Arrange
            var escenario = new Escenario();
            var operario = Operario.Crear("Juan", "hash-1111");
            escenario.Operarios.Setup(o => o.GetByIdAsync(operario.Id)).ReturnsAsync(operario);

            // Act
            var result = await escenario.Servicio().CambiarPinAsync(operario.Id, new CambiarPinDto { Pin = "2222" });

            // Assert
            Assert.True(result);
            Assert.Equal("hash-2222", operario.PinHash);
            escenario.Operarios.Verify(o => o.UpdateAsync(operario), Times.Once);
        }

        [Fact]
        public async Task CambiarPinAsync_PinOfAnotherActiveOperario_ThrowsConflictoDeUnicidad()
        {
            // Arrange
            var escenario = new Escenario();
            var operario = Operario.Crear("Juan", "hash-1111");
            escenario.Operarios.Setup(o => o.GetByIdAsync(operario.Id)).ReturnsAsync(operario);
            escenario.Operarios.Setup(o => o.GetActivoPorPinHashAsync("hash-2222")).ReturnsAsync(Operario.Crear("Otro", "hash-2222"));

            // Act & Assert
            await Assert.ThrowsAsync<ConflictoDeUnicidadException>(() => escenario.Servicio().CambiarPinAsync(operario.Id, new CambiarPinDto { Pin = "2222" }));
            Assert.Equal("hash-1111", operario.PinHash);
        }

        [Fact]
        public async Task CambiarPinAsync_SamePinAsItsOwn_IsNotAConflict()
        {
            // Arrange
            var escenario = new Escenario();
            var operario = Operario.Crear("Juan", "hash-1111");
            escenario.Operarios.Setup(o => o.GetByIdAsync(operario.Id)).ReturnsAsync(operario);
            escenario.Operarios.Setup(o => o.GetActivoPorPinHashAsync("hash-1111")).ReturnsAsync(operario);

            // Act
            var result = await escenario.Servicio().CambiarPinAsync(operario.Id, new CambiarPinDto { Pin = "1111" });

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task CambiarPinAsync_DeactivatedOperario_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var escenario = new Escenario();
            var operario = Operario.Crear("Juan", "hash-1111");
            operario.Desactivar();
            escenario.Operarios.Setup(o => o.GetByIdAsync(operario.Id)).ReturnsAsync(operario);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().CambiarPinAsync(operario.Id, new CambiarPinDto { Pin = "2222" }));

            // Assert
            Assert.Equal("id", ex.Campo);
        }
    }
}
