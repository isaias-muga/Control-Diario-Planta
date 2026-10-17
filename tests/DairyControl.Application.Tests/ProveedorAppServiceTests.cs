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
    public class ProveedorAppServiceTests
    {
        // Plant time is UTC-3: 15:00 UTC is 12:00 in Buenos Aires.
        private static readonly DateTime FechaValida = new(2026, 10, 15, 11, 0, 0);
        private static readonly TimeProvider Reloj = new RelojFijo(new DateTimeOffset(2026, 10, 15, 15, 0, 0, TimeSpan.Zero));

        private sealed class Escenario
        {
            public Mock<IProveedorRepository> Proveedores { get; } = new();
            public Mock<IRecepcionRepository> Recepciones { get; } = new();
            public Mock<IProveedorQueries> Queries { get; } = new();
            public Mock<IUsuarioActual> Actual { get; } = new();
            public Mock<IUsuarioRepository> Usuarios { get; } = new();
            public Mock<IOperarioRepository> Operarios { get; } = new();
            public Mock<IHasherDePin> HasherPin { get; } = new();
            public Guid ProveedorId { get; } = Guid.NewGuid();
            public Guid UsuarioId { get; } = Guid.NewGuid();

            public Escenario(bool proveedorExiste = true, int cantidad = 1, RolUsuario rol = RolUsuario.Admin)
            {
                Actual.Setup(a => a.Id).Returns(UsuarioId);
                Actual.Setup(a => a.Rol).Returns(rol);
                HasherPin.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(pin => "hash-" + pin);
                Proveedores.Setup(r => r.GetByIdAsync(ProveedorId))
                    .ReturnsAsync(proveedorExiste ? Proveedor.Crear("Proveedor Test", Guid.NewGuid()) : null);
                Queries.Setup(q => q.GetByIdAsync(ProveedorId))
                    .ReturnsAsync(new ProveedorDto { Id = ProveedorId, Nombre = "Proveedor Test", CantidadRecepciones = cantidad });
            }

            public ProveedorAppService Servicio() => new(Proveedores.Object, Recepciones.Object, Queries.Object, Actual.Object,
                new VerificacionPinService(Usuarios.Object, Operarios.Object, HasherPin.Object, Actual.Object, Reloj), Reloj);

            public void VerificarNadaGuardado() =>
                Recepciones.Verify(r => r.AddAsync(It.IsAny<RecepcionLeche>()), Times.Never);
        }

        private static RegistrarRecepcionDto Dto(int? ticket = null) => new()
        {
            Acidez = 17m,
            Temperatura = 5m,
            Litros = 1000m,
            FechaHora = FechaValida,
            Ticket = ticket
        };

        [Fact]
        public async Task GetByIdAsync_ProveedorDoesNotExist_ReturnsNull()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Queries.Setup(q => q.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((ProveedorDto?)null);

            // Act
            var result = await escenario.Servicio().GetByIdAsync(Guid.NewGuid());

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetByIdAsync_ProveedorExists_ReturnsNombreAndCount()
        {
            // Arrange
            var escenario = new Escenario(cantidad: 7);

            // Act
            var result = await escenario.Servicio().GetByIdAsync(escenario.ProveedorId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Proveedor Test", result.Nombre);
            Assert.Equal(7, result.CantidadRecepciones);
            escenario.Queries.Verify(q => q.GetByIdAsync(escenario.ProveedorId), Times.Once);
        }

        [Fact]
        public async Task GetAllAsync_ReturnsTheCountsOfTheQuery()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Queries.Setup(q => q.GetAllAsync()).ReturnsAsync(new List<ProveedorDto>
            {
                new() { Id = Guid.NewGuid(), Nombre = "A", CantidadRecepciones = 3 },
                new() { Id = Guid.NewGuid(), Nombre = "B", CantidadRecepciones = 0 }
            });

            // Act
            var result = await escenario.Servicio().GetAllAsync();

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal(3, result[0].CantidadRecepciones);
            escenario.Queries.Verify(q => q.GetAllAsync(), Times.Once);
            escenario.Proveedores.Verify(r => r.GetAllAsync(), Times.Never);
        }

        [Fact]
        public async Task CrearAsync_ValidDto_CallsAddAsyncAndReturnsDto()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            var result = await escenario.Servicio().CrearAsync(new CrearProveedorDto { Nombre = "Proveedor Test" });

            // Assert
            Assert.Equal("Proveedor Test", result.Nombre);
            Assert.Equal(0, result.CantidadRecepciones);
            escenario.Proveedores.Verify(r => r.AddAsync(It.Is<Proveedor>(p => p.CreadoPorUsuarioId == escenario.UsuarioId)), Times.Once);
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_ProveedorDoesNotExist_ReturnsNullAndNeverTouchesRecepciones()
        {
            // Arrange
            var escenario = new Escenario(proveedorExiste: false);

            // Act
            var result = await escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, new RegistrarRecepcionDto());

            // Assert
            Assert.Null(result);
            escenario.VerificarNadaGuardado();
            escenario.Recepciones.Verify(r => r.GetTicketRegistradoAsync(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_ProveedorExists_AddsOneRecepcionAndReturnsWhatWasSaved()
        {
            // Arrange
            var escenario = new Escenario(cantidad: 5);
            RecepcionLeche? agregada = null;
            escenario.Recepciones.Setup(r => r.AddAsync(It.IsAny<RecepcionLeche>()))
                .Callback<RecepcionLeche>(r => agregada = r).Returns(Task.CompletedTask);
            var dto = Dto();
            dto.Grasa = 3.5m;
            dto.Silo = 1;
            dto.Observaciones = "Observación de prueba";

            // Act
            var result = await escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, dto);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(agregada);
            Assert.Equal(agregada.Id, result.Id);
            Assert.Equal(1000m, result.Litros);
            Assert.Equal("Proveedor Test", result.ProveedorNombre);
            Assert.Null(result.OperarioNombre);
            Assert.Equal(escenario.ProveedorId, agregada.ProveedorId);
            Assert.Equal("Observación de prueba", agregada.Observaciones);
            escenario.Recepciones.Verify(r => r.AddAsync(It.IsAny<RecepcionLeche>()), Times.Once);
            escenario.Queries.Verify(q => q.GetByIdAsync(It.IsAny<Guid>()), Times.Never); // no count is computed for a registration
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_ByAdmin_StampsTheUserTheServerTimeAndNoOperario()
        {
            // Arrange
            var escenario = new Escenario();
            RecepcionLeche? agregada = null;
            escenario.Recepciones.Setup(r => r.AddAsync(It.IsAny<RecepcionLeche>()))
                .Callback<RecepcionLeche>(r => agregada = r).Returns(Task.CompletedTask);

            // Act
            await escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, Dto());

            // Assert
            Assert.NotNull(agregada);
            Assert.Equal(escenario.UsuarioId, agregada.RegistradoPorUsuarioId);
            Assert.Null(agregada.OperarioId);
            Assert.Equal(new DateTime(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc), agregada.RegistradoEn);
            escenario.Operarios.Verify(o => o.GetActivoPorPinHashAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_ByRecepcionWithValidPin_StampsTheOperarioAndReturnsItsName()
        {
            // Arrange
            var escenario = new Escenario(rol: RolUsuario.Recepcion);
            var usuario = Usuario.Crear("recepcion", "Celular recepción", RolUsuario.Recepcion, "hash");
            var operario = Operario.Crear("Juan Pérez", "hash-1234");
            escenario.Usuarios.Setup(u => u.GetByIdAsync(escenario.UsuarioId)).ReturnsAsync(usuario);
            escenario.Usuarios.AplicarModificaciones(usuario);
            escenario.Operarios.Setup(o => o.GetActivoPorPinHashAsync("hash-1234")).ReturnsAsync(operario);
            RecepcionLeche? agregada = null;
            escenario.Recepciones.Setup(r => r.AddAsync(It.IsAny<RecepcionLeche>()))
                .Callback<RecepcionLeche>(r => agregada = r).Returns(Task.CompletedTask);
            var dto = Dto();
            dto.Pin = "1234";

            // Act
            var result = await escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, dto);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Juan Pérez", result.OperarioNombre);
            Assert.NotNull(agregada);
            Assert.Equal(operario.Id, agregada.OperarioId);
            Assert.Equal(escenario.UsuarioId, agregada.RegistradoPorUsuarioId);
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_ByRecepcionWithoutPin_ThrowsReglaDeNegocioExceptionOnPinAndNeverSaves()
        {
            // Arrange
            var escenario = new Escenario(rol: RolUsuario.Recepcion);
            var usuarioRecepcion = Usuario.Crear("recepcion", "Celular recepción", RolUsuario.Recepcion, "hash");
            escenario.Usuarios.Setup(u => u.GetByIdAsync(escenario.UsuarioId)).ReturnsAsync(usuarioRecepcion);
            escenario.Usuarios.AplicarModificaciones(usuarioRecepcion);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, Dto()));

            // Assert
            Assert.Equal("pin", ex.Campo);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task GetOpcionesAsync_ReturnsIdAndNombreOnly()
        {
            // Arrange
            var escenario = new Escenario();
            var proveedor = Proveedor.Crear("Tambo Don Pedro", Guid.NewGuid());
            escenario.Proveedores.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Proveedor> { proveedor });

            // Act
            var result = await escenario.Servicio().GetOpcionesAsync();

            // Assert
            var opcion = Assert.Single(result);
            Assert.Equal(proveedor.Id, opcion.Id);
            Assert.Equal("Tambo Don Pedro", opcion.Nombre);
            escenario.Queries.Verify(q => q.GetAllAsync(), Times.Never);
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_WithAguaAndTicket_StoresThemInTheRecepcion()
        {
            // Arrange
            var escenario = new Escenario();
            RecepcionLeche? agregada = null;
            escenario.Recepciones.Setup(r => r.AddAsync(It.IsAny<RecepcionLeche>()))
                .Callback<RecepcionLeche>(r => agregada = r).Returns(Task.CompletedTask);
            var dto = Dto(4501);
            dto.AguaPorcentaje = 2m;

            // Act
            await escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, dto);

            // Assert
            Assert.NotNull(agregada);
            Assert.Equal(2m, agregada.Parametros.AguaPorcentaje);
            Assert.Equal(4501, agregada.Ticket);
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_InvalidAgua_ThrowsAndNeverSaves()
        {
            // Arrange
            var escenario = new Escenario();
            var dto = Dto();
            dto.AguaPorcentaje = 120m;

            // Act & Assert
            await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, dto));
            escenario.VerificarNadaGuardado();
        }

        [Theory]
        [InlineData("grasa")]
        [InlineData("acidez")]
        [InlineData("temperatura")]
        [InlineData("litros")]
        [InlineData("silo")]
        public async Task RegistrarRecepcionAsync_ImpossibleValue_ThrowsAndNeverSaves(string campo)
        {
            // Arrange
            var escenario = new Escenario();
            var dto = Dto();
            dto.Grasa = 3.5m;
            dto.Silo = 2;
            switch (campo)
            {
                case "grasa": dto.Grasa = 12m; break;
                case "acidez": dto.Acidez = 5m; break;
                case "temperatura": dto.Temperatura = 60m; break;
                case "litros": dto.Litros = 45000m; break;
                case "silo": dto.Silo = 99; break;
            }

            // Act & Assert
            await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, dto));
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_ValueOutsideQualityLimitsButPlausible_IsAccepted()
        {
            // Arrange: acidity 28 °D and 35 °C are far above the default quality limits but plausible
            var escenario = new Escenario();
            var dto = Dto();
            dto.Acidez = 28m;
            dto.Temperatura = 35m;

            // Act
            var result = await escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, dto);

            // Assert
            Assert.NotNull(result);
            escenario.Recepciones.Verify(r => r.AddAsync(It.IsAny<RecepcionLeche>()), Times.Once);
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_TicketUsedByAnotherProveedor_ThrowsTicketDuplicadoAndNeverSaves()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Recepciones.Setup(r => r.GetTicketRegistradoAsync(6311))
                .ReturnsAsync(new TicketRegistrado("Tambo Las Acacias", new DateTime(2026, 10, 11, 11, 6, 0)));

            // Act
            var ex = await Assert.ThrowsAsync<TicketDuplicadoException>(() =>
                escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, Dto(6311)));

            // Assert
            Assert.Equal("El ticket 6311 ya está registrado (Tambo Las Acacias, 11/10/2026)", ex.Message);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_UnusedTicket_ChecksOnceAndRegisters()
        {
            // Arrange
            var escenario = new Escenario();
            escenario.Recepciones.Setup(r => r.GetTicketRegistradoAsync(7000)).ReturnsAsync((TicketRegistrado?)null);

            // Act
            var result = await escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, Dto(7000));

            // Assert
            Assert.NotNull(result);
            escenario.Recepciones.Verify(r => r.GetTicketRegistradoAsync(7000), Times.Once);
            escenario.Recepciones.Verify(r => r.AddAsync(It.Is<RecepcionLeche>(x => x.Ticket == 7000)), Times.Once);
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_NoTicket_NeverChecksTicket()
        {
            // Arrange
            var escenario = new Escenario();

            // Act
            await escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, Dto());

            // Assert
            escenario.Recepciones.Verify(r => r.GetTicketRegistradoAsync(It.IsAny<int>()), Times.Never);
            escenario.Recepciones.Verify(r => r.AddAsync(It.IsAny<RecepcionLeche>()), Times.Once);
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_NonPositiveTicket_ThrowsReglaDeNegocioExceptionWithoutChecking()
        {
            // Arrange
            var escenario = new Escenario();

            // Act & Assert
            await Assert.ThrowsAsync<ReglaDeNegocioException>(() =>
                escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, Dto(0)));
            escenario.Recepciones.Verify(r => r.GetTicketRegistradoAsync(It.IsAny<int>()), Times.Never);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_UniqueViolationOnSave_ThrowsTicketDuplicadoWithDetails()
        {
            // Arrange: the pre-check passes, then another operator wins the race
            var escenario = new Escenario();
            escenario.Recepciones.SetupSequence(r => r.GetTicketRegistradoAsync(6311))
                .ReturnsAsync((TicketRegistrado?)null)
                .ReturnsAsync(new TicketRegistrado("Tambo Las Acacias", new DateTime(2026, 10, 15, 11, 50, 0)));
            escenario.Recepciones.Setup(r => r.AddAsync(It.IsAny<RecepcionLeche>()))
                .ThrowsAsync(new TicketDuplicadoException("El ticket ya está registrado."));

            // Act
            var ex = await Assert.ThrowsAsync<TicketDuplicadoException>(() =>
                escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, Dto(6311)));

            // Assert
            Assert.Equal("El ticket 6311 ya está registrado (Tambo Las Acacias, 15/10/2026)", ex.Message);
            escenario.Recepciones.Verify(r => r.AddAsync(It.IsAny<RecepcionLeche>()), Times.Once);
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_FechaHoraMissing_ThrowsReglaDeNegocioExceptionAndDoesNotSave()
        {
            // Arrange
            var escenario = new Escenario();
            var dto = Dto();
            dto.FechaHora = null;

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, dto));

            // Assert
            Assert.Equal("fechaHora", ex.Campo);
            escenario.VerificarNadaGuardado();
        }

        [Theory]
        [InlineData("1999-12-31T23:59:00")]
        [InlineData("2026-10-15T13:01:00")] // plant now is 12:00: more than 1 hour ahead
        public async Task RegistrarRecepcionAsync_FechaHoraOutOfRange_ThrowsReglaDeNegocioExceptionAndDoesNotSave(string fechaHora)
        {
            // Arrange
            var escenario = new Escenario();
            var dto = Dto();
            dto.FechaHora = DateTime.Parse(fechaHora);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, dto));

            // Assert
            Assert.Equal("fechaHora", ex.Campo);
            escenario.VerificarNadaGuardado();
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_FechaHoraWithinTheNextHourOfPlantTime_Registers()
        {
            // Arrange
            var escenario = new Escenario();
            var dto = Dto();
            dto.FechaHora = new DateTime(2026, 10, 15, 13, 0, 0);

            // Act
            var result = await escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, dto);

            // Assert
            Assert.NotNull(result);
            escenario.Recepciones.Verify(r => r.AddAsync(It.IsAny<RecepcionLeche>()), Times.Once);
        }

        [Fact]
        public async Task RegistrarRecepcionAsync_FechaHoraInUtc_IsConvertedToPlantTimeBeforeValidating()
        {
            // Arrange: 15:30 UTC is 12:30 plant time, within the next hour of the 12:00 plant now
            var escenario = new Escenario();
            var dto = Dto();
            dto.FechaHora = new DateTime(2026, 10, 15, 15, 30, 0, DateTimeKind.Utc);

            // Act
            await escenario.Servicio().RegistrarRecepcionAsync(escenario.ProveedorId, dto);

            // Assert
            escenario.Recepciones.Verify(r => r.AddAsync(It.Is<RecepcionLeche>(x => x.FechaHora == new DateTime(2026, 10, 15, 12, 30, 0))), Times.Once);
        }
    }
}
