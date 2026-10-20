using DairyControl.Domain.Exceptions;
using DairyControl.Api.Controllers;
using DairyControl.Application.DTOs;
using DairyControl.Application.Queries;
using DairyControl.Application.Seguridad;
using DairyControl.Application.Services;
using DairyControl.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace DairyControl.Api.Tests
{
    public class RecepcionesControllerTests
    {
        private static RecepcionesController NuevoControlador(Mock<IRecepcionQueries> mockQueries) =>
            new(new ResumenRecepcionesAppService(mockQueries.Object), new ListadoRecepcionesAppService(mockQueries.Object),
                new RecepcionAppService(new Mock<IRecepcionRepository>().Object, new Mock<IProveedorRepository>().Object, new Mock<IConfiguracionCalidadRepository>().Object,
                    new Mock<IHistorialQueries>().Object, new Mock<IUsuarioActual>().Object, TimeProvider.System));

        [Fact]
        public async Task GetListadoAsync_ValidQuery_ReturnsOkAndPassesEveryFilter()
        {
            // Arrange
            var mockQueries = new Mock<IRecepcionQueries>();
            var esperado = new ListadoRecepcionesDto { Pagina = 2 };
            var proveedorId = Guid.NewGuid();
            mockQueries
                .Setup(q => q.GetListadoAsync(It.IsAny<FiltroRecepciones>()))
                .ReturnsAsync(esperado);
            var controller = NuevoControlador(mockQueries);

            // Act
            var resultado = await controller.GetListadoAsync(
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), proveedorId, 4980, true, true, 2);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(resultado.Result);
            Assert.Same(esperado, ok.Value);
            mockQueries.Verify(q => q.GetListadoAsync(It.Is<FiltroRecepciones>(f =>
                f.Desde == new DateOnly(2026, 10, 1)
                && f.Hasta == new DateOnly(2026, 10, 31)
                && f.ProveedorId == proveedorId
                && f.Ticket == 4980
                && f.SoloFueraDeRango
                && f.IncluirAnuladas
                && f.Pagina == 2)), Times.Once);
        }

        [Fact]
        public async Task GetListadoAsync_InvalidRange_ThrowsReglaDeNegocioExceptionForTheMiddlewareAndDoesNotQuery()
        {
            // Arrange
            var mockQueries = new Mock<IRecepcionQueries>();
            var controller = NuevoControlador(mockQueries);

            // Act
            var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => controller.GetListadoAsync(
                new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 1), null, null, false, false, 1));

            // Assert
            Assert.Equal("desde", ex.Campo);
            mockQueries.Verify(q => q.GetListadoAsync(It.IsAny<FiltroRecepciones>()), Times.Never);
        }

        [Fact]
        public async Task GetSiguienteTicketAsync_Called_ReturnsOkWithTheSuggestedTicket()
        {
            // Arrange
            var mockQueries = new Mock<IRecepcionQueries>();
            mockQueries.Setup(q => q.GetSiguienteTicketAsync()).ReturnsAsync((int?)4981);
            var controller = NuevoControlador(mockQueries);

            // Act
            var resultado = await controller.GetSiguienteTicketAsync();

            // Assert
            var ok = Assert.IsType<OkObjectResult>(resultado.Result);
            var dto = Assert.IsType<SiguienteTicketDto>(ok.Value);
            Assert.Equal(4981, dto.Ticket);
            mockQueries.Verify(q => q.GetSiguienteTicketAsync(), Times.Once);
        }
    }
}
