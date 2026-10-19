using DairyControl.Application.DTOs;
using DairyControl.Application.Queries;
using DairyControl.Application.Seguridad;
using DairyControl.Application.Services;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using DairyControl.Domain.ValueObjects;
using Moq;

namespace DairyControl.Application.Tests
{
    public class RecepcionAppServiceTests
    {
        // Plant time is UTC-3: 15:00 UTC is 12:00 in Buenos Aires.
        private static readonly DateTime FechaOriginal = new(2026, 10, 15, 6, 0, 0);
        private static readonly TimeProvider Reloj = new RelojFijo(new DateTimeOffset(2026, 10, 15, 15, 0, 0, TimeSpan.Zero));
        private static readonly byte[] VersionLeida = [1, 2, 3, 4, 5, 6, 7, 8];
        private static readonly string VersionTexto = Convert.ToBase64String(VersionLeida);

        private sealed class Escenario
        {
            public Mock<IRecepcionRepository> Recepciones { get; } = new();
            public Mock<IProveedorRepository> Proveedores { get; } = new();
            public Mock<IConfiguracionCalidadRepository> Configuracion { get; } = new();
            public Mock<IHistorialQueries> Historial { get; } = new();
            public Mock<IUsuarioActual> Actual { get; } = new();
            public Guid AdminId { get; } = Guid.NewGuid();
            public Proveedor Proveedor { get; } = Proveedor.Crear("Tambo Viejo", Guid.NewGuid());
            public RecepcionLeche Recepcion { get; }

            public Escenario(int? ticket = 4980, int silo = 1, bool anulada = false)
            {
                Recepcion = RecepcionLeche.Registrar(Proveedor.Id, ParametrosCalidad.Create(3.5m, 18m, 4m, 1000m, 1m), FechaOriginal, FechaOriginal.AddHours(6), silo,
                    "Primera carga", Guid.NewGuid(), null, new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc), ticket);
                if (anulada)
                {
                    Recepcion.Anular(MotivoDeCambio.Create("Se cargó dos veces"), Guid.NewGuid(), new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc));
                }
                Actual.Setup(a => a.Id).Returns(AdminId);
                Actual.Setup(a => a.Rol).Returns(RolUsuario.Admin);
                Configuracion.Setup(c => c.GetAsync()).ReturnsAsync(
                    new ConfiguracionLeida(ConfiguracionCalidad.Crear(LimitesCalidad.Predeterminados, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), cantidadSilos: 3), [1]));
                Recepciones.Setup(r => r.GetByIdAsync(Recepcion.Id)).ReturnsAsync(new RecepcionLeida(Recepcion, VersionLeida));
                Recepciones.Setup(r => r.UpdateAsync(It.IsAny<RecepcionLeche>(), It.IsAny<byte[]?>(), It.IsAny<IReadOnlyList<CambioRegistrado>>())).ReturnsAsync([9, 9, 9, 9, 9, 9, 9, 9]);
                Proveedores.Setup(p => p.GetByIdAsync(Proveedor.Id)).ReturnsAsync(Proveedor);
            }

            public RecepcionAppService Servicio() => new(Recepciones.Object, Proveedores.Object, Configuracion.Object, Historial.Object, Actual.Object, Reloj);

            public CorregirRecepcionDto Dto(decimal litros = 1000m, int? ticket = 4980, int? silo = 1, Guid? proveedorId = null, string? motivo = "Error de tipeo en la balanza",
                string? version = null, string? observaciones = "Primera carga") => new()
            {
                ProveedorId = proveedorId ?? Proveedor.Id,
                Grasa = 3.5m,
                Acidez = 18m,
                Temperatura = 4m,
                Litros = litros,
                AguaPorcentaje = 1m,
                FechaHora = FechaOriginal,
                Silo = silo,
                Observaciones = observaciones,
                Ticket = ticket,
                Motivo = motivo,
                Version = version ?? VersionTexto
            };

            public void VerificarNadaGuardado() =>
                Recepciones.Verify(r => r.UpdateAsync(It.IsAny<RecepcionLeche>(), It.IsAny<byte[]?>(), It.IsAny<IReadOnlyList<CambioRegistrado>>()), Times.Never);
        }

        [Fact]
        public async Task CorregirAsync_ChangeLitros_SavesWithTheVersionReadAndOneHistoryEntryAndReturnsTheNewVersion()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var resultado = await escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(litros: 1250m));

            // Assert
            Assert.NotNull(resultado);
            Assert.Equal(1250m, resultado.Litros);
            Assert.Equal(Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 }), resultado.Version);
            escenario.Recepciones.Verify(r => r.UpdateAsync(escenario.Recepcion, It.Is<byte[]?>(v => v != null && v.SequenceEqual(VersionLeida)),
                It.Is<IReadOnlyList<CambioRegistrado>>(c => c.Count == 1 && c[0].Campo == "litros" && c[0].ValorAnterior == "1000" && c[0].ValorNuevo == "1250"
                    && c[0].Motivo == "Error de tipeo en la balanza" && c[0].UsuarioId == escenario.AdminId
                    && c[0].EnUtc == new DateTime(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc))), Times.Once);
        }

        [Fact]
        public async Task CorregirAsync_RecepcionDoesNotExist_ReturnsNullAndSavesNothing()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var resultado = await escenario.Servicio().CorregirAsync(Guid.NewGuid(), escenario.Dto(litros: 1250m));

            // Assert
            Assert.Null(resultado);
            escenario.VerificarNadaGuardado();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("no es base64!!")]
        public async Task CorregirAsync_MissingOrInvalidVersion_Throws400OnVersionAndSavesNothing(string? version)
        {
            // Arrange
            var escenario = new Escenario();
            var dto = escenario.Dto(litros: 1250m);
            dto.Version = version;

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, dto));

            // Assert
            Assert.Equal("version", ex.Campo);
            escenario.VerificarNadaGuardado();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        [InlineData("cort")]
        public async Task CorregirAsync_MissingOrShortReason_Throws400OnMotivoAndSavesNothing(string? motivo)
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(litros: 1250m, motivo: motivo)));

            // Assert
            Assert.Equal("motivo", ex.Campo);
            Assert.Equal(1000m, escenario.Recepcion.Parametros.Litros);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task CorregirAsync_NothingChanged_Throws400AndSavesNothing()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto()));

            // Assert
            Assert.Equal("cambios", ex.Campo);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task CorregirAsync_ImplausibleLitros_Throws400OnLitrosAndSavesNothing()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(litros: 99999m)));

            // Assert
            Assert.Equal("litros", ex.Campo);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task CorregirAsync_NewTicketAlreadyUsed_ThrowsTicketDuplicadoWithTheDetailsAndSavesNothing()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Recepciones.Setup(r => r.GetTicketRegistradoAsync(5001)).ReturnsAsync(new TicketRegistrado("Tambo Otro", new DateTime(2026, 10, 3, 8, 0, 0)));

            // Act
            var ex = await Assert.ThrowsAsync<TicketDuplicadoException>(() => escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(ticket: 5001)));

            // Assert
            Assert.Contains("El ticket 5001 ya está registrado (Tambo Otro, 03/10/2026)", ex.Message);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task CorregirAsync_NewTicketBelongsToAVoidedRecepcion_MessageSaysItCannotBeReused()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Recepciones.Setup(r => r.GetTicketRegistradoAsync(5001)).ReturnsAsync(new TicketRegistrado("Tambo Otro", new DateTime(2026, 10, 3, 8, 0, 0), anulada: true));

            // Act
            var ex = await Assert.ThrowsAsync<TicketDuplicadoException>(() => escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(ticket: 5001)));

            // Assert
            Assert.Contains("anulada", ex.Message);
            Assert.Contains("no se puede reutilizar", ex.Message);
        }

        [Fact]
        public async Task CorregirAsync_TicketUnchanged_DoesNotCheckItsUniqueness()
        {
            // Arrange: the ticket is its own, so it would "exist" if it were checked
            var escenario = new Escenario();
            escenario.Recepciones.Setup(r => r.GetTicketRegistradoAsync(4980)).ReturnsAsync(new TicketRegistrado("Tambo Viejo", FechaOriginal));

            // Act
            var resultado = await escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(litros: 1100m));

            // Assert
            Assert.NotNull(resultado);
            escenario.Recepciones.Verify(r => r.GetTicketRegistradoAsync(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task CorregirAsync_LostTheTicketRace_ThrowsTicketDuplicadoWithTheWinnersDetails()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Recepciones.SetupSequence(r => r.GetTicketRegistradoAsync(5001))
                .ReturnsAsync((TicketRegistrado?)null)
                .ReturnsAsync(new TicketRegistrado("Tambo Ganador", new DateTime(2026, 10, 15, 7, 0, 0)));
            escenario.Recepciones.Setup(r => r.UpdateAsync(It.IsAny<RecepcionLeche>(), It.IsAny<byte[]?>(), It.IsAny<IReadOnlyList<CambioRegistrado>>()))
                .ThrowsAsync(new TicketDuplicadoException("El ticket ya está registrado."));

            // Act
            var ex = await Assert.ThrowsAsync<TicketDuplicadoException>(() => escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(ticket: 5001)));

            // Assert
            Assert.Contains("Tambo Ganador", ex.Message);
        }

        [Fact]
        public async Task CorregirAsync_StaleVersion_PropagatesTheConcurrencyConflict()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Recepciones.Setup(r => r.UpdateAsync(It.IsAny<RecepcionLeche>(), It.IsAny<byte[]?>(), It.IsAny<IReadOnlyList<CambioRegistrado>>()))
                .ThrowsAsync(new ConflictoDeConcurrenciaException());

            // Act & Assert
            await Assert.ThrowsAsync<ConflictoDeConcurrenciaException>(() => escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(litros: 1250m)));
        }

        [Fact]
        public async Task CorregirAsync_SiloAboveTheConfiguredCount_Throws400OnSilo()
        {
            // Arrange: the plant has 3 silos
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(silo: 4)));

            // Assert
            Assert.Equal("silo", ex.Campo);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task CorregirAsync_SiloUnchangedEvenIfAboveTheCountNow_IsNotReChecked()
        {
            // Arrange: the recepción was loaded in silo 5, then the plant went down to 3 silos
            var escenario = new Escenario(silo: 5);

            // Act
            var resultado = await escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(litros: 1100m, silo: 5));

            // Assert
            Assert.NotNull(resultado);
            Assert.Equal(5, resultado.Silo);
        }

        [Fact]
        public async Task CorregirAsync_ChangeToAnInactiveProveedor_Throws400OnProveedorId()
        {
            // Arrange
            var escenario = new Escenario();
            var inactivo = Proveedor.Crear("Tambo Cerrado", Guid.NewGuid());
            inactivo.Desactivar(MotivoDeCambio.Create("Cerró el tambo"), Guid.NewGuid(), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
            escenario.Proveedores.Setup(p => p.GetByIdAsync(inactivo.Id)).ReturnsAsync(inactivo);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(proveedorId: inactivo.Id)));

            // Assert
            Assert.Equal("proveedorId", ex.Campo);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task CorregirAsync_ProveedorStaysTheSameEvenIfNowInactive_IsAllowedAndOtherFieldsCanBeFixed()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Proveedor.Desactivar(MotivoDeCambio.Create("Cerró el tambo"), Guid.NewGuid(), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

            // Act
            var resultado = await escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(litros: 1100m));

            // Assert
            Assert.NotNull(resultado);
            Assert.False(resultado.ProveedorActivo);
        }

        [Fact]
        public async Task CorregirAsync_ChangeProveedor_RecordsBothNames()
        {
            // Arrange
            var escenario = new Escenario();
            var otro = Proveedor.Crear("Tambo Nuevo", Guid.NewGuid());
            escenario.Proveedores.Setup(p => p.GetByIdAsync(otro.Id)).ReturnsAsync(otro);

            // Act
            var resultado = await escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(proveedorId: otro.Id));

            // Assert
            Assert.NotNull(resultado);
            Assert.Equal("Tambo Nuevo", resultado.ProveedorNombre);
            escenario.Recepciones.Verify(r => r.UpdateAsync(escenario.Recepcion, It.IsAny<byte[]?>(),
                It.Is<IReadOnlyList<CambioRegistrado>>(c => c.Count == 1 && c[0].Campo == "proveedor" && c[0].ValorAnterior == "Tambo Viejo" && c[0].ValorNuevo == "Tambo Nuevo")), Times.Once);
        }

        [Fact]
        public async Task CorregirAsync_ProveedorDoesNotExist_Throws400OnProveedorId()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(proveedorId: Guid.NewGuid())));

            // Assert
            Assert.Equal("proveedorId", ex.Campo);
        }

        [Fact]
        public async Task CorregirAsync_VoidedRecepcion_Throws400AndSavesNothing()
        {
            // Arrange
            var escenario = new Escenario(anulada: true);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().CorregirAsync(escenario.Recepcion.Id, escenario.Dto(litros: 1100m)));

            // Assert
            Assert.Contains("anulada", ex.Message);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task AnularAsync_ValidReason_VoidsItAndSavesTheEntryWithoutAClientVersion()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var anulada = await escenario.Servicio().AnularAsync(escenario.Recepcion.Id, new MotivoCambioDto { Motivo = "Se cargó dos veces" });

            // Assert
            Assert.True(anulada);
            Assert.True(escenario.Recepcion.EstaAnulada);
            Assert.Equal("Se cargó dos veces", escenario.Recepcion.MotivoAnulacion);
            escenario.Recepciones.Verify(r => r.UpdateAsync(escenario.Recepcion, null,
                It.Is<IReadOnlyList<CambioRegistrado>>(c => c.Count == 1 && c[0].Campo == "anulacion" && c[0].UsuarioId == escenario.AdminId)), Times.Once);
        }

        [Fact]
        public async Task AnularAsync_RecepcionDoesNotExist_ReturnsFalse()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var anulada = await escenario.Servicio().AnularAsync(Guid.NewGuid(), new MotivoCambioDto { Motivo = "Se cargó dos veces" });

            // Assert
            Assert.False(anulada);
            escenario.VerificarNadaGuardado();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("abc")]
        public async Task AnularAsync_MissingOrShortReason_Throws400OnMotivoAndKeepsItValid(string? motivo)
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().AnularAsync(escenario.Recepcion.Id, new MotivoCambioDto { Motivo = motivo }));

            // Assert
            Assert.Equal("motivo", ex.Campo);
            Assert.False(escenario.Recepcion.EstaAnulada);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task AnularAsync_AlreadyVoided_Throws400()
        {
            // Arrange
            var escenario = new Escenario(anulada: true);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().AnularAsync(escenario.Recepcion.Id, new MotivoCambioDto { Motivo = "Se cargó dos veces" }));

            // Assert
            Assert.Contains("ya está anulada", ex.Message);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task GetDetalleAsync_RecepcionExists_ReturnsItsValuesAndVersion()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var detalle = await escenario.Servicio().GetDetalleAsync(escenario.Recepcion.Id);

            // Assert
            Assert.NotNull(detalle);
            Assert.Equal(1000m, detalle.Litros);
            Assert.Equal("Tambo Viejo", detalle.ProveedorNombre);
            Assert.Equal(4980, detalle.Ticket);
            Assert.Equal(VersionTexto, detalle.Version);
            Assert.False(detalle.Anulada);
        }

        [Fact]
        public async Task GetDetalleAsync_RecepcionDoesNotExist_ReturnsNull()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var detalle = await escenario.Servicio().GetDetalleAsync(Guid.NewGuid());

            // Assert
            Assert.Null(detalle);
        }

        [Fact]
        public async Task GetHistorialAsync_AsksTheHistoryOfARecepcion()
        {
            // Arrange
            var escenario = new Escenario();
            IReadOnlyList<CorreccionDto> esperado = [new CorreccionDto { Motivo = "x" }];
            escenario.Historial.Setup(h => h.GetAsync(CambioRegistrado.EntidadRecepcion, escenario.Recepcion.Id)).ReturnsAsync(esperado);

            // Act
            var historial = await escenario.Servicio().GetHistorialAsync(escenario.Recepcion.Id);

            // Assert
            Assert.Same(esperado, historial);
        }
    }
}
