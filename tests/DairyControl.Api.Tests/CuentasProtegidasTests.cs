using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DairyControl.Application.DTOs;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;
using Moq;

namespace DairyControl.Api.Tests
{
    // The whole HTTP path of the protected (owner) accounts, the last-Admin rule, reactivation and the history of usuarios and operarios:
    // policy, body, the service and the answers. Who may call each endpoint is in the endpoint x role matrix (AutorizacionTests).
    public class CuentasProtegidasTests : IClassFixture<FabricaDeApi>
    {
        private static readonly DateTime Antes = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly FabricaDeApi _fabrica;

        public CuentasProtegidasTests(FabricaDeApi fabrica)
        {
            _fabrica = fabrica;
        }

        private Usuario Dueno()
        {
            var dueno = _fabrica.CrearUsuario(RolUsuario.Admin);
            dueno.MarcarProtegido(true);
            return dueno;
        }

        private static async Task<JsonElement> Json(HttpResponseMessage respuesta) =>
            await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        private void VerificarNadaGuardado() =>
            _fabrica.Usuarios.Verify(u => u.GuardarCambioAsync(It.IsAny<Usuario>(), It.IsAny<CambioRegistrado>(), It.IsAny<bool>()), Times.Never);

        [Fact]
        public async Task Desactivar_ProtectedAccountByAnotherAdmin_Returns403WithAClearMessageAndSavesNothing()
        {
            // Arrange
            var dueno = Dueno();
            _fabrica.Usuarios.Invocations.Clear();
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Usuarios/{dueno.Id}/desactivar", new { motivo = "Ya no trabaja acá" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.Contains("protegida", (await Json(respuesta)).GetProperty("error").GetString());
            Assert.True(dueno.Activo);
            VerificarNadaGuardado();
        }

        [Fact]
        public async Task CambiarRol_ProtectedAccountByAnotherAdmin_Returns403AndKeepsTheRole()
        {
            // Arrange
            var dueno = Dueno();
            _fabrica.Usuarios.Invocations.Clear();
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PutAsJsonAsync($"/api/Usuarios/{dueno.Id}/rol", new { rol = "Consulta", motivo = "Probando el cambio" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.Equal(RolUsuario.Admin, dueno.Rol);
            VerificarNadaGuardado();
        }

        [Fact]
        public async Task RestablecerPassword_ProtectedAccountByAnotherAdmin_Returns403AndKeepsThePassword()
        {
            // Arrange
            var dueno = Dueno();
            var hash = dueno.PasswordHash;
            _fabrica.Usuarios.Invocations.Clear();
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Usuarios/{dueno.Id}/restablecer-password", new { password = "una-clave-temporal" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.Equal(hash, dueno.PasswordHash);
            VerificarNadaGuardado();
        }

        [Fact]
        public async Task RestablecerPassword_ProtectedAccountOwnPassword_Returns204()
        {
            // Arrange
            var dueno = Dueno();
            var cliente = _fabrica.ClienteDe(dueno);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Usuarios/{dueno.Id}/restablecer-password", new { password = "una-clave-temporal" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        }

        [Fact]
        public async Task CambiarRol_TheLastActiveAdmin_Returns400WithTheMessageAndTheHistoryEntryIsNotKept()
        {
            // Arrange: the repository refuses inside its serializable transaction (see UsuarioRepository) when no other Admin is active
            var admin = _fabrica.CrearUsuario(RolUsuario.Admin);
            var otro = _fabrica.CrearUsuario(RolUsuario.Admin);
            _fabrica.Usuarios.Setup(u => u.GuardarCambioAsync(otro, It.IsAny<CambioRegistrado>(), true))
                .ThrowsAsync(new ReglaDeNegocioException(Usuario.MensajeUltimoAdmin, "id"));
            var cliente = _fabrica.ClienteDe(admin);

            // Act
            var respuesta = await cliente.PutAsJsonAsync($"/api/Usuarios/{otro.Id}/rol", new { rol = "Oficina", motivo = "Cambio de tareas" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            var cuerpo = await Json(respuesta);
            Assert.Equal(Usuario.MensajeUltimoAdmin, cuerpo.GetProperty("error").GetString());
            Assert.Equal("id", cuerpo.GetProperty("field").GetString());
        }

        [Fact]
        public async Task Desactivar_WithoutReason_Returns400OnMotivo()
        {
            // Arrange
            var admin = _fabrica.CrearUsuario(RolUsuario.Admin);
            var otro = _fabrica.CrearUsuario(RolUsuario.Oficina);
            var cliente = _fabrica.ClienteDe(admin);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Usuarios/{otro.Id}/desactivar", new { }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Equal("motivo", (await Json(respuesta)).GetProperty("field").GetString());
            Assert.True(otro.Activo);
        }

        [Fact]
        public async Task Desactivar_OtherUserWithReason_Returns204AndSavesTheEntry()
        {
            // Arrange
            var admin = _fabrica.CrearUsuario(RolUsuario.Admin);
            var otro = _fabrica.CrearUsuario(RolUsuario.Oficina);
            var cliente = _fabrica.ClienteDe(admin);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Usuarios/{otro.Id}/desactivar", new { motivo = "Dejó la empresa" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
            Assert.False(otro.Activo);
            _fabrica.Usuarios.Verify(u => u.GuardarCambioAsync(otro,
                It.Is<CambioRegistrado>(c => c.UsuarioId == admin.Id && c.Motivo == "Dejó la empresa" && c.EntidadId == otro.Id), false), Times.Once);
        }

        [Fact]
        public async Task Reactivar_InactiveUser_Returns204AndTheUserMustChangeThePassword()
        {
            // Arrange
            var admin = _fabrica.CrearUsuario(RolUsuario.Admin);
            var otro = _fabrica.CrearUsuario(RolUsuario.Oficina);
            otro.Desactivar(MotivoDeCambio.Create("Baja de prueba"), admin.Id, Antes);
            var cliente = _fabrica.ClienteDe(admin);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Usuarios/{otro.Id}/reactivar", new { motivo = "Volvió a trabajar" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
            Assert.True(otro.Activo);
            Assert.True(otro.DebeCambiarPassword);
        }

        [Fact]
        public async Task Reactivar_UnknownUser_Returns404()
        {
            // Arrange
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Usuarios/{Guid.NewGuid()}/reactivar", new { motivo = "Volvió a trabajar" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        }

        [Fact]
        public async Task GetUsuarios_ShowsTheProtectedFlagButNoRequestCanSetIt()
        {
            // Arrange
            var dueno = Dueno();
            _fabrica.Usuarios.Setup(u => u.GetAllAsync()).ReturnsAsync([dueno]);
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var lista = await Json(await cliente.GetAsync("/api/Usuarios", TestContext.Current.CancellationToken));
            var creacion = await cliente.PostAsJsonAsync("/api/Usuarios",
                new { username = "nuevo.usuario", nombre = "Nuevo", rol = "Oficina", password = "temporal-123", protegido = true }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(lista[0].GetProperty("protegido").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, creacion.StatusCode);
            Assert.False((await Json(creacion)).GetProperty("protegido").GetBoolean());
        }

        [Fact]
        public async Task HistorialDeUsuario_KnownAndUnknown_Returns200And404()
        {
            // Arrange
            var usuario = _fabrica.CrearUsuario(RolUsuario.Oficina);
            IReadOnlyList<CorreccionDto> historial = [new CorreccionDto { Motivo = "Alta de usuario", Usuario = "Administrador", Cambios = [new CampoCambiadoDto { Campo = "rol", ValorNuevo = "Oficina" }] }];
            _fabrica.Historial.Setup(h => h.GetAsync(CambioRegistrado.EntidadUsuario, usuario.Id)).ReturnsAsync(historial);
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var conocido = await cliente.GetAsync($"/api/Usuarios/{usuario.Id}/historial", TestContext.Current.CancellationToken);
            var desconocido = await cliente.GetAsync($"/api/Usuarios/{Guid.NewGuid()}/historial", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, conocido.StatusCode);
            Assert.Equal("Alta de usuario", (await Json(conocido))[0].GetProperty("motivo").GetString());
            Assert.Equal(HttpStatusCode.NotFound, desconocido.StatusCode);
        }

        [Fact]
        public async Task ReactivarOperario_PinUsedByAnotherActiveOperario_Returns409AndStaysInactive()
        {
            // Arrange
            var operario = Operario.Crear("Juan", "hash-1234");
            operario.Desactivar(MotivoDeCambio.Create("Baja de prueba"), Guid.NewGuid(), Antes);
            _fabrica.Operarios.Setup(o => o.GetByIdAsync(operario.Id)).ReturnsAsync(operario);
            _fabrica.Operarios.Setup(o => o.GetActivoPorPinHashAsync("hash-1234")).ReturnsAsync(Operario.Crear("Otro", "hash-1234"));
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Operarios/{operario.Id}/reactivar", new { motivo = "Volvió a trabajar" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
            Assert.False(operario.Activo);
        }

        [Fact]
        public async Task ReactivarOperario_FreePin_Returns204()
        {
            // Arrange
            var operario = Operario.Crear("Pedro", "hash-5678");
            operario.Desactivar(MotivoDeCambio.Create("Baja de prueba"), Guid.NewGuid(), Antes);
            _fabrica.Operarios.Setup(o => o.GetByIdAsync(operario.Id)).ReturnsAsync(operario);
            var cliente = _fabrica.ClienteDe(RolUsuario.Admin);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Operarios/{operario.Id}/reactivar", new { motivo = "Volvió a trabajar" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
            Assert.True(operario.Activo);
        }
    }
}
