using DairyControl.Application.DTOs;
using DairyControl.Application.Queries;
using DairyControl.Application.Seguridad;
using DairyControl.Application.Services;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using DairyControl.Domain.ValueObjects;
using Moq;

namespace DairyControl.Application.Tests
{
    public class OperarioAppServiceTests
    {
        private static readonly DateTimeOffset Ahora = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

        private sealed class Escenario
        {
            public Mock<IOperarioRepository> Operarios { get; } = new();
            public Mock<IHasherDePin> Hasher { get; } = new();
            public Mock<IUsuarioActual> Actual { get; } = new();
            public Mock<IHistorialQueries> Historial { get; } = new();
            public Guid ActualId { get; } = Guid.NewGuid();

            public Escenario()
            {
                Hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => "hash-" + p);
                Actual.Setup(a => a.Id).Returns(ActualId);
            }

            public OperarioAppService Servicio() => new(Operarios.Object, Hasher.Object, Actual.Object, Historial.Object, new RelojFijo(Ahora));
        }

        private static MotivoCambioDto Motivo(string texto = "Dejó de trabajar") => new() { Motivo = texto };

        [Fact]
        public async Task CrearAsync_ValidPin_StoresOnlyTheHashAndTheCreationEntry()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var result = await escenario.Servicio().CrearAsync(new CrearOperarioDto { Nombre = "Juan Pérez", Pin = "1234" });

            // Assert
            Assert.Equal("Juan Pérez", result.Nombre);
            Assert.True(result.Activo);
            escenario.Operarios.Verify(o => o.AddAsync(It.Is<Operario>(x => x.PinHash == "hash-1234"),
                It.Is<CambioRegistrado>(c => c.Entidad == CambioRegistrado.EntidadOperario && c.Campo == CambioRegistrado.Campos.Alta
                    && c.UsuarioId == escenario.ActualId && c.EnUtc == Ahora.UtcDateTime)), Times.Once);
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
            escenario.Operarios.Verify(o => o.AddAsync(It.IsAny<Operario>(), It.IsAny<CambioRegistrado>()), Times.Never);
        }

        [Fact]
        public async Task CrearAsync_PinOfAnotherActiveOperario_ThrowsConflictoDeUnicidad()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Operarios.Setup(o => o.GetActivoPorPinHashAsync("hash-1234")).ReturnsAsync(Operario.Crear("Otro", "hash-1234"));

            // Act & Assert
            await Assert.ThrowsAsync<ConflictoDeUnicidadException>(() => escenario.Servicio().CrearAsync(new CrearOperarioDto { Nombre = "Juan", Pin = "1234" }));
            escenario.Operarios.Verify(o => o.AddAsync(It.IsAny<Operario>(), It.IsAny<CambioRegistrado>()), Times.Never);
        }

        [Fact]
        public async Task DesactivarAsync_ExistingOperario_DeactivatesAndSavesTheEntryWithTheReason()
        {
            // Arrange
            var escenario = new Escenario();
            var operario = Operario.Crear("Juan", "hash-1234");
            escenario.Operarios.Setup(o => o.GetByIdAsync(operario.Id)).ReturnsAsync(operario);

            // Act
            var result = await escenario.Servicio().DesactivarAsync(operario.Id, Motivo("Se fue de la planta"));

            // Assert
            Assert.True(result);
            Assert.False(operario.Activo);
            escenario.Operarios.Verify(o => o.UpdateAsync(operario,
                It.Is<CambioRegistrado>(c => c.Campo == CambioRegistrado.Campos.Activo && c.Motivo == "Se fue de la planta" && c.UsuarioId == escenario.ActualId)), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("abcd")]
        public async Task DesactivarAsync_MissingOrShortReason_ThrowsOnMotivoAndKeepsItActive(string? motivo)
        {
            // Arrange
            var escenario = new Escenario();
            var operario = Operario.Crear("Juan", "hash-1234");
            escenario.Operarios.Setup(o => o.GetByIdAsync(operario.Id)).ReturnsAsync(operario);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().DesactivarAsync(operario.Id, new MotivoCambioDto { Motivo = motivo }));

            // Assert
            Assert.Equal("motivo", ex.Campo);
            Assert.True(operario.Activo);
            escenario.Operarios.Verify(o => o.UpdateAsync(It.IsAny<Operario>(), It.IsAny<CambioRegistrado>()), Times.Never);
        }

        [Fact]
        public async Task DesactivarAsync_UnknownOperario_ReturnsFalse()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var result = await escenario.Servicio().DesactivarAsync(Guid.NewGuid(), Motivo());

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ReactivarAsync_InactiveOperarioWithFreePin_ReactivatesAndSavesTheEntry()
        {
            // Arrange
            var escenario = new Escenario();
            var operario = Operario.Crear("Juan", "hash-1234");
            operario.Desactivar(MotivoDeCambio.Create("Baja de prueba"), Guid.NewGuid(), Ahora.UtcDateTime);
            escenario.Operarios.Setup(o => o.GetByIdAsync(operario.Id)).ReturnsAsync(operario);

            // Act
            var result = await escenario.Servicio().ReactivarAsync(operario.Id, Motivo("Volvió a trabajar"));

            // Assert
            Assert.True(result);
            Assert.True(operario.Activo);
            escenario.Operarios.Verify(o => o.UpdateAsync(operario,
                It.Is<CambioRegistrado>(c => c.ValorNuevo == "Activo" && c.Motivo == "Volvió a trabajar")), Times.Once);
        }

        [Fact]
        public async Task ReactivarAsync_PinNowUsedByAnotherActiveOperario_ThrowsConflictoDeUnicidadAndStaysInactive()
        {
            // Arrange
            var escenario = new Escenario();
            var operario = Operario.Crear("Juan", "hash-1234");
            operario.Desactivar(MotivoDeCambio.Create("Baja de prueba"), Guid.NewGuid(), Ahora.UtcDateTime);
            escenario.Operarios.Setup(o => o.GetByIdAsync(operario.Id)).ReturnsAsync(operario);
            escenario.Operarios.Setup(o => o.GetActivoPorPinHashAsync("hash-1234")).ReturnsAsync(Operario.Crear("Otro", "hash-1234"));

            // Act
            await Assert.ThrowsAsync<ConflictoDeUnicidadException>(() => escenario.Servicio().ReactivarAsync(operario.Id, Motivo()));

            // Assert
            Assert.False(operario.Activo);
            escenario.Operarios.Verify(o => o.UpdateAsync(It.IsAny<Operario>(), It.IsAny<CambioRegistrado>()), Times.Never);
        }

        [Fact]
        public async Task ReactivarAsync_AlreadyActive_ThrowsReglaDeNegocioException()
        {
            // Arrange
            var escenario = new Escenario();
            var operario = Operario.Crear("Juan", "hash-1234");
            escenario.Operarios.Setup(o => o.GetByIdAsync(operario.Id)).ReturnsAsync(operario);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().ReactivarAsync(operario.Id, Motivo()));

            // Assert
            Assert.Equal("id", ex.Campo);
        }

        [Fact]
        public async Task ReactivarAsync_UnknownOperario_ReturnsFalse()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var result = await escenario.Servicio().ReactivarAsync(Guid.NewGuid(), Motivo());

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task CambiarPinAsync_FreePin_ReplacesTheHashAndSavesAnEntryWithoutIt()
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
            escenario.Operarios.Verify(o => o.UpdateAsync(operario,
                It.Is<CambioRegistrado>(c => c.Campo == CambioRegistrado.Campos.Pin && c.ValorNuevo != null && !c.ValorNuevo.Contains("2222") && c.UsuarioId == escenario.ActualId)), Times.Once);
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
            operario.Desactivar(MotivoDeCambio.Create("Baja de prueba"), Guid.NewGuid(), Ahora.UtcDateTime);
            escenario.Operarios.Setup(o => o.GetByIdAsync(operario.Id)).ReturnsAsync(operario);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().CambiarPinAsync(operario.Id, new CambiarPinDto { Pin = "2222" }));

            // Assert
            Assert.Equal("id", ex.Campo);
        }

        [Fact]
        public async Task GetHistorialAsync_ReturnsWhatTheQueryReturnsForTheOperarioEntity()
        {
            // Arrange
            var escenario = new Escenario();
            var id = Guid.NewGuid();
            IReadOnlyList<CorreccionDto> historial = [new CorreccionDto { Motivo = "Alta de operario" }];
            escenario.Historial.Setup(h => h.GetAsync(CambioRegistrado.EntidadOperario, id)).ReturnsAsync(historial);

            // Act
            var result = await escenario.Servicio().GetHistorialAsync(id);

            // Assert
            Assert.Same(historial, result);
        }
    }
}
