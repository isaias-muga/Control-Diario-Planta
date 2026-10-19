using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DairyControl.Application.DTOs;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using DairyControl.Domain.ValueObjects;
using Moq;

namespace DairyControl.Api.Tests
{
    // The whole HTTP path of the corrections, voids and deactivations: policy, body, the service and the answers. Who may do what is also
    // in the endpoint x role matrix (AutorizacionTests); here the point is what is saved, what is refused and how it is answered.
    public class CorreccionesYAnulacionTests : IClassFixture<FabricaDeApi>
    {
        private static readonly byte[] VersionLeida = [1, 2, 3, 4, 5, 6, 7, 8];
        private static readonly string VersionTexto = Convert.ToBase64String(VersionLeida);
        private static readonly DateTime FechaOriginal = DateTime.UtcNow.Date.AddDays(-3).AddHours(6);

        private readonly FabricaDeApi _fabrica;

        public CorreccionesYAnulacionTests(FabricaDeApi fabrica)
        {
            _fabrica = fabrica;
        }

        private (RecepcionLeche Recepcion, Proveedor Proveedor) PrepararRecepcion(int? ticket = 4980)
        {
            var proveedor = Proveedor.Crear("Tambo Viejo", Guid.NewGuid());
            var recepcion = RecepcionLeche.Registrar(proveedor.Id, ParametrosCalidad.Create(null, 18m, 4m, 1000m), FechaOriginal, DateTime.UtcNow, 1, null, Guid.NewGuid(), null, DateTime.UtcNow, ticket);
            _fabrica.Recepciones.Reset();
            _fabrica.Recepciones.Setup(r => r.GetByIdAsync(recepcion.Id)).ReturnsAsync(new RecepcionLeida(recepcion, VersionLeida));
            _fabrica.Recepciones.Setup(r => r.UpdateAsync(It.IsAny<RecepcionLeche>(), It.IsAny<byte[]?>(), It.IsAny<IReadOnlyList<CambioRegistrado>>())).ReturnsAsync([9, 9, 9, 9, 9, 9, 9, 9]);
            _fabrica.Proveedores.Setup(p => p.GetByIdAsync(proveedor.Id)).ReturnsAsync(proveedor);
            return (recepcion, proveedor);
        }

        private static object Cuerpo(Guid proveedorId, decimal litros = 1250m, int? ticket = 4980, string? motivo = "Error de tipeo en la balanza", string? version = null) => new
        {
            proveedorId,
            acidez = 18,
            temperatura = 4,
            litros,
            silo = 1,
            ticket,
            fechaHora = FechaOriginal.ToString("yyyy-MM-ddTHH:mm:ss"),
            motivo,
            version = version ?? VersionTexto
        };

        private void VerificarNadaGuardado() =>
            _fabrica.Recepciones.Verify(r => r.UpdateAsync(It.IsAny<RecepcionLeche>(), It.IsAny<byte[]?>(), It.IsAny<IReadOnlyList<CambioRegistrado>>()), Times.Never);

        private static async Task<JsonElement> Json(HttpResponseMessage respuesta) =>
            await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        [Fact]
        public async Task Put_AdminCorrectsLitros_Returns200WithTheNewValuesAndVersionAndSavesOneHistoryEntry()
        {
            // Arrange
            var (recepcion, proveedor) = PrepararRecepcion();
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PutAsJsonAsync($"/api/Recepciones/{recepcion.Id}", Cuerpo(proveedor.Id), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            var cuerpo = await Json(respuesta);
            Assert.Equal(1250m, cuerpo.GetProperty("litros").GetDecimal());
            Assert.Equal(Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 }), cuerpo.GetProperty("version").GetString());
            _fabrica.Recepciones.Verify(r => r.UpdateAsync(recepcion, It.Is<byte[]?>(v => v != null && v.SequenceEqual(VersionLeida)),
                It.Is<IReadOnlyList<CambioRegistrado>>(c => c.Count == 1 && c[0].Campo == "litros" && c[0].ValorNuevo == "1250")), Times.Once);
        }

        [Fact]
        public async Task Put_WithoutReason_Returns400OnMotivoAndSavesNothing()
        {
            // Arrange
            var (recepcion, proveedor) = PrepararRecepcion();
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PutAsJsonAsync($"/api/Recepciones/{recepcion.Id}", Cuerpo(proveedor.Id, motivo: null), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            var cuerpo = await Json(respuesta);
            Assert.Equal("motivo", cuerpo.GetProperty("field").GetString());
            VerificarNadaGuardado();
        }

        [Fact]
        public async Task Put_WithoutVersion_Returns400OnVersionAndSavesNothing()
        {
            // Arrange
            var (recepcion, proveedor) = PrepararRecepcion();
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);
            var cuerpo = new { proveedorId = proveedor.Id, acidez = 18, temperatura = 4, litros = 1250, silo = 1, ticket = 4980, fechaHora = FechaOriginal.ToString("yyyy-MM-ddTHH:mm:ss"), motivo = "Error de tipeo" };

            // Act
            var respuesta = await cliente.PutAsJsonAsync($"/api/Recepciones/{recepcion.Id}", cuerpo, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Equal("version", (await Json(respuesta)).GetProperty("field").GetString());
            VerificarNadaGuardado();
        }

        [Fact]
        public async Task Put_StaleVersion_Returns409()
        {
            // Arrange
            var (recepcion, proveedor) = PrepararRecepcion();
            _fabrica.Recepciones.Setup(r => r.UpdateAsync(It.IsAny<RecepcionLeche>(), It.IsAny<byte[]?>(), It.IsAny<IReadOnlyList<CambioRegistrado>>()))
                .ThrowsAsync(new ConflictoDeConcurrenciaException());
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PutAsJsonAsync($"/api/Recepciones/{recepcion.Id}", Cuerpo(proveedor.Id), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        }

        [Fact]
        public async Task Put_TicketOfAVoidedRecepcion_Returns409SayingItCannotBeReused()
        {
            // Arrange
            var (recepcion, proveedor) = PrepararRecepcion();
            _fabrica.Recepciones.Setup(r => r.GetTicketRegistradoAsync(5001)).ReturnsAsync(new TicketRegistrado("Tambo Otro", new DateTime(2026, 10, 3, 8, 0, 0), anulada: true));
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PutAsJsonAsync($"/api/Recepciones/{recepcion.Id}", Cuerpo(proveedor.Id, ticket: 5001), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
            Assert.Contains("no se puede reutilizar", (await Json(respuesta)).GetProperty("error").GetString());
            VerificarNadaGuardado();
        }

        [Fact]
        public async Task Put_RecepcionDoesNotExist_Returns404()
        {
            // Arrange
            var (_, proveedor) = PrepararRecepcion();
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PutAsJsonAsync($"/api/Recepciones/{Guid.NewGuid()}", Cuerpo(proveedor.Id), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        }

        [Fact]
        public async Task Get_AdminReadsTheRecepcionForTheCorrectionForm_ReturnsItsValuesAndVersion()
        {
            // Arrange
            var (recepcion, _) = PrepararRecepcion();
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.GetAsync($"/api/Recepciones/{recepcion.Id}", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            var cuerpo = await Json(respuesta);
            Assert.Equal(1000m, cuerpo.GetProperty("litros").GetDecimal());
            Assert.Equal(VersionTexto, cuerpo.GetProperty("version").GetString());
        }

        [Fact]
        public async Task PostAnular_AdminWithReason_Returns204AndSavesTheVoid()
        {
            // Arrange
            var (recepcion, _) = PrepararRecepcion();
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Recepciones/{recepcion.Id}/anular", new { motivo = "Se cargó dos veces" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
            Assert.True(recepcion.EstaAnulada);
            _fabrica.Recepciones.Verify(r => r.UpdateAsync(recepcion, null, It.Is<IReadOnlyList<CambioRegistrado>>(c => c.Count == 1 && c[0].Campo == "anulacion")), Times.Once);
        }

        [Fact]
        public async Task PostAnular_WithoutReason_Returns400OnMotivoAndKeepsItValid()
        {
            // Arrange
            var (recepcion, _) = PrepararRecepcion();
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Recepciones/{recepcion.Id}/anular", new { motivo = "  " }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Equal("motivo", (await Json(respuesta)).GetProperty("field").GetString());
            Assert.False(recepcion.EstaAnulada);
            VerificarNadaGuardado();
        }

        [Fact]
        public async Task PostAnular_AlreadyVoided_Returns400()
        {
            // Arrange
            var (recepcion, _) = PrepararRecepcion();
            recepcion.Anular(MotivoDeCambio.Create("Se cargó dos veces"), Guid.NewGuid(), DateTime.UtcNow);
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Recepciones/{recepcion.Id}/anular", new { motivo = "Otra vez" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            VerificarNadaGuardado();
        }

        [Theory]
        [InlineData(RolUsuario.Oficina)]
        [InlineData(RolUsuario.Consulta)]
        [InlineData(RolUsuario.Recepcion)]
        public async Task CorrectVoidAndDeactivate_ByARoleThatMayNotChangeData_Get403AndNothingIsSaved(RolUsuario rol)
        {
            // Arrange
            var (recepcion, proveedor) = PrepararRecepcion();
            _fabrica.Proveedores.Invocations.Clear();
            var cliente = _fabrica.ClienteDe(rol);

            // Act
            var corregir = await cliente.PutAsJsonAsync($"/api/Recepciones/{recepcion.Id}", Cuerpo(proveedor.Id), TestContext.Current.CancellationToken);
            var anular = await cliente.PostAsJsonAsync($"/api/Recepciones/{recepcion.Id}/anular", new { motivo = "Se cargó dos veces" }, TestContext.Current.CancellationToken);
            var detalle = await cliente.GetAsync($"/api/Recepciones/{recepcion.Id}", TestContext.Current.CancellationToken);
            var renombrar = await cliente.PutAsJsonAsync($"/api/Proveedores/{proveedor.Id}", new { nombre = "Otro", motivo = "Corrección", version = VersionTexto }, TestContext.Current.CancellationToken);
            var desactivar = await cliente.PostAsJsonAsync($"/api/Proveedores/{proveedor.Id}/desactivar", new { motivo = "Cerró el tambo" }, TestContext.Current.CancellationToken);
            var reactivar = await cliente.PostAsJsonAsync($"/api/Proveedores/{proveedor.Id}/reactivar", new { motivo = "Volvió a entregar" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.All(new[] { corregir, anular, detalle, renombrar, desactivar, reactivar }, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
            VerificarNadaGuardado();
            Assert.False(recepcion.EstaAnulada);
            _fabrica.Proveedores.Verify(p => p.UpdateAsync(It.IsAny<Proveedor>(), It.IsAny<byte[]?>(), It.IsAny<CambioRegistrado>()), Times.Never);
        }

        [Theory]
        [InlineData(RolUsuario.Admin)]
        [InlineData(RolUsuario.Oficina)]
        [InlineData(RolUsuario.Consulta)]
        public async Task GetHistorial_ReadRoles_Get200WithTheCorrections(RolUsuario rol)
        {
            // Arrange
            var id = Guid.NewGuid();
            IReadOnlyList<CorreccionDto> historial =
            [
                new CorreccionDto { CorreccionId = Guid.NewGuid(), EnUtc = DateTime.UtcNow, Usuario = "Ana", Motivo = "Error de tipeo",
                    Cambios = [new CampoCambiadoDto { Campo = "litros", ValorAnterior = "1000", ValorNuevo = "1250" }] }
            ];
            _fabrica.Historial.Setup(h => h.GetAsync(CambioRegistrado.EntidadRecepcion, id)).ReturnsAsync(historial);
            var cliente = _fabrica.ClienteDe(rol);

            // Act
            var respuesta = await cliente.GetAsync($"/api/Recepciones/{id}/historial", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            var cuerpo = await Json(respuesta);
            Assert.Equal("Error de tipeo", cuerpo[0].GetProperty("motivo").GetString());
            Assert.Equal("1250", cuerpo[0].GetProperty("cambios")[0].GetProperty("valorNuevo").GetString());
        }

        [Fact]
        public async Task GetHistorial_RecepcionDoesNotExist_Returns404AndTheReceptionPhoneGets403()
        {
            // Arrange
            _fabrica.Historial.Setup(h => h.GetAsync(CambioRegistrado.EntidadRecepcion, It.IsAny<Guid>())).ReturnsAsync((IReadOnlyList<CorreccionDto>?)null);

            // Act
            var noExiste = await _fabrica.ClienteDe(RolUsuario.Admin).GetAsync($"/api/Recepciones/{Guid.NewGuid()}/historial", TestContext.Current.CancellationToken);
            var telefono = await _fabrica.ClienteDe(RolUsuario.Recepcion).GetAsync($"/api/Recepciones/{Guid.NewGuid()}/historial", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, noExiste.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, telefono.StatusCode);
        }

        [Fact]
        public async Task Proveedores_AdminRenamesDeactivatesAndReactivates_Returns200And204AndSavesEachWithItsEntry()
        {
            // Arrange
            var proveedor = Proveedor.Crear("Tambo Don Pedro", Guid.NewGuid());
            _fabrica.Proveedores.Reset();
            _fabrica.Proveedores.Setup(p => p.GetLeidoAsync(proveedor.Id)).ReturnsAsync(new ProveedorLeido(proveedor, VersionLeida));
            _fabrica.Proveedores.Setup(p => p.UpdateAsync(It.IsAny<Proveedor>(), It.IsAny<byte[]?>(), It.IsAny<CambioRegistrado>())).ReturnsAsync([9, 9, 9, 9, 9, 9, 9, 9]);
            _fabrica.ProveedorQueries.Reset();
            _fabrica.ProveedorQueries.Setup(q => q.GetByIdAsync(proveedor.Id)).ReturnsAsync(() => new ProveedorDto { Id = proveedor.Id, Nombre = proveedor.Nombre, Activo = proveedor.Activo, CantidadRecepciones = 2 });
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var renombrar = await cliente.PutAsJsonAsync($"/api/Proveedores/{proveedor.Id}", new { nombre = "Tambo Don Pedro S.A.", motivo = "Mal escrito", version = VersionTexto }, TestContext.Current.CancellationToken);
            var desactivar = await cliente.PostAsJsonAsync($"/api/Proveedores/{proveedor.Id}/desactivar", new { motivo = "Cerró el tambo" }, TestContext.Current.CancellationToken);
            var reactivar = await cliente.PostAsJsonAsync($"/api/Proveedores/{proveedor.Id}/reactivar", new { motivo = "Volvió a entregar" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, renombrar.StatusCode);
            Assert.Equal("Tambo Don Pedro S.A.", (await Json(renombrar)).GetProperty("nombre").GetString());
            Assert.Equal(HttpStatusCode.NoContent, desactivar.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, reactivar.StatusCode);
            _fabrica.Proveedores.Verify(p => p.UpdateAsync(proveedor, It.IsAny<byte[]?>(), It.IsAny<CambioRegistrado>()), Times.Exactly(3));
            Assert.True(proveedor.Activo);
            Assert.Equal("Tambo Don Pedro S.A.", proveedor.Nombre);
        }

        [Fact]
        public async Task Proveedores_DeactivateWithoutReason_Returns400OnMotivo()
        {
            // Arrange
            var proveedor = Proveedor.Crear("Tambo Don Pedro", Guid.NewGuid());
            _fabrica.Proveedores.Reset();
            _fabrica.Proveedores.Setup(p => p.GetLeidoAsync(proveedor.Id)).ReturnsAsync(new ProveedorLeido(proveedor, VersionLeida));
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Proveedores/{proveedor.Id}/desactivar", new { }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Equal("motivo", (await Json(respuesta)).GetProperty("field").GetString());
            Assert.True(proveedor.Activo);
        }

        [Theory]
        [InlineData("/api/Recepciones")]
        [InlineData("/api/Proveedores")]
        public async Task Delete_OnRecepcionesOrProveedores_IsNeverAccepted_NotEvenForAdmin(string ruta)
        {
            // Arrange
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.DeleteAsync($"{ruta}/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

            // Assert: the route exists for GET and PUT, DELETE is not one of its methods
            Assert.Equal(HttpStatusCode.MethodNotAllowed, respuesta.StatusCode);
        }
    }
}
