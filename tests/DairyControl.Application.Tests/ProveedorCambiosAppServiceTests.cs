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
    public class ProveedorCambiosAppServiceTests
    {
        private static readonly TimeProvider Reloj = new RelojFijo(new DateTimeOffset(2026, 10, 15, 15, 0, 0, TimeSpan.Zero));
        private static readonly byte[] VersionLeida = [1, 2, 3, 4, 5, 6, 7, 8];
        private static readonly string VersionTexto = Convert.ToBase64String(VersionLeida);

        private sealed class Escenario
        {
            public Mock<IProveedorRepository> Proveedores { get; } = new();
            public Mock<IProveedorQueries> Queries { get; } = new();
            public Mock<IHistorialQueries> Historial { get; } = new();
            public Mock<IUsuarioActual> Actual { get; } = new();
            public Guid AdminId { get; } = Guid.NewGuid();
            public Proveedor Proveedor { get; } = Proveedor.Crear("Tambo Don Pedro", Guid.NewGuid());

            public Escenario()
            {
                Actual.Setup(a => a.Id).Returns(AdminId);
                Proveedores.Setup(p => p.GetLeidoAsync(Proveedor.Id)).ReturnsAsync(new ProveedorLeido(Proveedor, VersionLeida));
                Proveedores.Setup(p => p.UpdateAsync(It.IsAny<Proveedor>(), It.IsAny<byte[]?>(), It.IsAny<CambioRegistrado>())).ReturnsAsync([9, 9, 9, 9, 9, 9, 9, 9]);
                Queries.Setup(q => q.GetByIdAsync(Proveedor.Id)).ReturnsAsync(() => new ProveedorDto { Id = Proveedor.Id, Nombre = Proveedor.Nombre, Activo = Proveedor.Activo, CantidadRecepciones = 3 });
            }

            public ProveedorCambiosAppService Servicio() => new(Proveedores.Object, Queries.Object, Historial.Object, Actual.Object, Reloj);

            public void VerificarNadaGuardado() =>
                Proveedores.Verify(p => p.UpdateAsync(It.IsAny<Proveedor>(), It.IsAny<byte[]?>(), It.IsAny<CambioRegistrado>()), Times.Never);
        }

        private static RenombrarProveedorDto Renombrar(string? nombre = "Tambo Don Pedro S.A.", string? motivo = "El nombre estaba mal escrito", string? version = null) =>
            new() { Nombre = nombre, Motivo = motivo, Version = version ?? VersionTexto };

        [Fact]
        public async Task RenombrarAsync_ValidName_SavesWithTheVersionAndTheHistoryEntryAndReturnsTheProveedor()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var resultado = await escenario.Servicio().RenombrarAsync(escenario.Proveedor.Id, Renombrar());

            // Assert
            Assert.NotNull(resultado);
            Assert.Equal("Tambo Don Pedro S.A.", resultado.Nombre);
            Assert.Equal(3, resultado.CantidadRecepciones);
            escenario.Proveedores.Verify(p => p.UpdateAsync(escenario.Proveedor, It.Is<byte[]?>(v => v != null && v.SequenceEqual(VersionLeida)),
                It.Is<CambioRegistrado>(c => c.Campo == "nombre" && c.ValorAnterior == "Tambo Don Pedro" && c.ValorNuevo == "Tambo Don Pedro S.A."
                    && c.Motivo == "El nombre estaba mal escrito" && c.UsuarioId == escenario.AdminId)), Times.Once);
        }

        [Fact]
        public async Task RenombrarAsync_ProveedorDoesNotExist_ReturnsNullAndSavesNothing()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var resultado = await escenario.Servicio().RenombrarAsync(Guid.NewGuid(), Renombrar());

            // Assert
            Assert.Null(resultado);
            escenario.VerificarNadaGuardado();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("cort")]
        public async Task RenombrarAsync_MissingOrShortReason_Throws400OnMotivoAndKeepsTheName(string? motivo)
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().RenombrarAsync(escenario.Proveedor.Id, Renombrar(motivo: motivo)));

            // Assert
            Assert.Equal("motivo", ex.Campo);
            Assert.Equal("Tambo Don Pedro", escenario.Proveedor.Nombre);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task RenombrarAsync_MissingVersion_Throws400OnVersion()
        {
            // Arrange
            var escenario = new Escenario();
            var dto = Renombrar();
            dto.Version = null;

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().RenombrarAsync(escenario.Proveedor.Id, dto));

            // Assert
            Assert.Equal("version", ex.Campo);
            escenario.VerificarNadaGuardado();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        public async Task RenombrarAsync_BlankName_Throws400OnNombre(string? nombre)
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().RenombrarAsync(escenario.Proveedor.Id, Renombrar(nombre: nombre)));

            // Assert
            Assert.Equal("nombre", ex.Campo);
        }

        [Fact]
        public async Task RenombrarAsync_StaleVersion_PropagatesTheConcurrencyConflict()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Proveedores.Setup(p => p.UpdateAsync(It.IsAny<Proveedor>(), It.IsAny<byte[]?>(), It.IsAny<CambioRegistrado>())).ThrowsAsync(new ConflictoDeConcurrenciaException());

            // Act & Assert
            await Assert.ThrowsAsync<ConflictoDeConcurrenciaException>(() => escenario.Servicio().RenombrarAsync(escenario.Proveedor.Id, Renombrar()));
        }

        [Fact]
        public async Task DesactivarAsync_ActiveProveedor_DeactivatesAndSavesTheEntry()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var resultado = await escenario.Servicio().DesactivarAsync(escenario.Proveedor.Id, new MotivoCambioDto { Motivo = "Dejó de entregar" });

            // Assert
            Assert.True(resultado);
            Assert.False(escenario.Proveedor.Activo);
            escenario.Proveedores.Verify(p => p.UpdateAsync(escenario.Proveedor, null,
                It.Is<CambioRegistrado>(c => c.Campo == "activo" && c.ValorAnterior == "Activo" && c.ValorNuevo == "Inactivo" && c.Motivo == "Dejó de entregar")), Times.Once);
        }

        [Fact]
        public async Task DesactivarAsync_ProveedorDoesNotExist_ReturnsFalse()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var resultado = await escenario.Servicio().DesactivarAsync(Guid.NewGuid(), new MotivoCambioDto { Motivo = "Dejó de entregar" });

            // Assert
            Assert.False(resultado);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task DesactivarAsync_WithoutReason_Throws400OnMotivoAndStaysActive()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().DesactivarAsync(escenario.Proveedor.Id, new MotivoCambioDto()));

            // Assert
            Assert.Equal("motivo", ex.Campo);
            Assert.True(escenario.Proveedor.Activo);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task ReactivarAsync_InactiveProveedor_ReactivatesAndSavesTheEntry()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Proveedor.Desactivar(MotivoDeCambio.Create("Dejó de entregar"), Guid.NewGuid(), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

            // Act
            var resultado = await escenario.Servicio().ReactivarAsync(escenario.Proveedor.Id, new MotivoCambioDto { Motivo = "Volvió a entregar" });

            // Assert
            Assert.True(resultado);
            Assert.True(escenario.Proveedor.Activo);
            escenario.Proveedores.Verify(p => p.UpdateAsync(escenario.Proveedor, null,
                It.Is<CambioRegistrado>(c => c.ValorAnterior == "Inactivo" && c.ValorNuevo == "Activo" && c.Motivo == "Volvió a entregar")), Times.Once);
        }

        [Fact]
        public async Task ReactivarAsync_AlreadyActive_Throws400AndSavesNothing()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().ReactivarAsync(escenario.Proveedor.Id, new MotivoCambioDto { Motivo = "Volvió a entregar" }));

            // Assert
            Assert.Contains("ya está activo", ex.Message);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task GetHistorialAsync_AsksTheHistoryOfAProveedor()
        {
            // Arrange
            var escenario = new Escenario();
            IReadOnlyList<CorreccionDto> esperado = [new CorreccionDto { Motivo = "x" }];
            escenario.Historial.Setup(h => h.GetAsync(CambioRegistrado.EntidadProveedor, escenario.Proveedor.Id)).ReturnsAsync(esperado);

            // Act
            var historial = await escenario.Servicio().GetHistorialAsync(escenario.Proveedor.Id);

            // Assert
            Assert.Same(esperado, historial);
        }
    }
}
