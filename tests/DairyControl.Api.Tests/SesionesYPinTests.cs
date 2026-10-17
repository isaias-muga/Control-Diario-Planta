using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Infrastructure.Security;
using Moq;

namespace DairyControl.Api.Tests
{
    // Login, the PIN of the reception phone and its lockout, through the real pipeline.
    public class SesionesYPinTests : IClassFixture<FabricaDeApi>
    {
        private readonly FabricaDeApi _fabrica;

        public SesionesYPinTests(FabricaDeApi fabrica)
        {
            _fabrica = fabrica;
        }

        private static string Hash(string password) => new PasswordHasherPbkdf2().Hash(password);

        [Fact]
        public async Task Login_RightPassword_ReturnsATokenThatOpensTheApi()
        {
            // Arrange
            var usuario = _fabrica.CrearUsuario(RolUsuario.Oficina, hash: Hash("clave-correcta"), username: "oficina.uno");
            var cliente = _fabrica.CreateClient();

            // Act
            var login = await cliente.PostAsJsonAsync("/api/Auth/login", new { username = "oficina.uno", password = "clave-correcta" }, TestContext.Current.CancellationToken);
            var cuerpo = await login.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            cliente.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cuerpo.GetProperty("token").GetString());
            var lectura = await cliente.GetAsync("/api/Proveedores", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.False(cuerpo.GetProperty("debeCambiarPassword").GetBoolean());
            Assert.NotEqual(HttpStatusCode.Unauthorized, lectura.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, lectura.StatusCode);
            Assert.NotNull(usuario);
        }

        [Fact]
        public async Task Login_WrongPasswordAndUnknownUser_GiveTheSameAnswer()
        {
            // Arrange
            _fabrica.CrearUsuario(RolUsuario.Oficina, hash: Hash("clave-correcta"), username: "oficina.dos");
            var cliente = _fabrica.CreateClient();

            // Act
            var mala = await cliente.PostAsJsonAsync("/api/Auth/login", new { username = "oficina.dos", password = "otra" }, TestContext.Current.CancellationToken);
            var inexistente = await cliente.PostAsJsonAsync("/api/Auth/login", new { username = "no.existe", password = "otra" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, mala.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, inexistente.StatusCode);
            Assert.Equal(await mala.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), await inexistente.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Login_FiveWrongPasswords_LocksTheUserEvenForTheRightOne()
        {
            // Arrange
            _fabrica.CrearUsuario(RolUsuario.Oficina, hash: Hash("clave-correcta"), username: "oficina.tres");
            var cliente = _fabrica.CreateClient();
            for (var i = 0; i < Usuario.MaxFallosLogin; i++)
            {
                await cliente.PostAsJsonAsync("/api/Auth/login", new { username = "oficina.tres", password = "mala" }, TestContext.Current.CancellationToken);
            }

            // Act
            var respuesta = await cliente.PostAsJsonAsync("/api/Auth/login", new { username = "oficina.tres", password = "clave-correcta" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        }

        [Fact]
        public async Task Login_UserWhoMustChangeThePassword_IsToldSo()
        {
            // Arrange
            _fabrica.CrearUsuario(RolUsuario.Recepcion, debeCambiarPassword: true, hash: Hash("temporal-123"), username: "recepcion.nuevo");

            // Act
            var login = await _fabrica.CreateClient().PostAsJsonAsync("/api/Auth/login", new { username = "recepcion.nuevo", password = "temporal-123" }, TestContext.Current.CancellationToken);

            // Assert
            var cuerpo = await login.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.True(cuerpo.GetProperty("debeCambiarPassword").GetBoolean());
        }

        [Fact]
        public async Task ChangePassword_Valid_ReturnsAFreshTokenThatWorksAndKillsTheOldOne()
        {
            // Arrange
            var usuario = _fabrica.CrearUsuario(RolUsuario.Oficina, debeCambiarPassword: true, hash: Hash("temporal-123"));
            var viejo = _fabrica.ClienteDe(usuario);

            // Act
            var cambio = await viejo.PostAsJsonAsync("/api/Auth/cambiar-password",
                new { passwordActual = "temporal-123", passwordNueva = "mi-clave-nueva" }, TestContext.Current.CancellationToken);
            var cuerpo = await cambio.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            var nuevo = _fabrica.CreateClient();
            nuevo.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cuerpo.GetProperty("token").GetString());
            var conElNuevo = await nuevo.GetAsync("/api/Proveedores", TestContext.Current.CancellationToken);
            var conElViejo = await viejo.GetAsync("/api/Proveedores", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
            Assert.False(cuerpo.GetProperty("debeCambiarPassword").GetBoolean());
            Assert.NotEqual(HttpStatusCode.Forbidden, conElNuevo.StatusCode);
            Assert.NotEqual(HttpStatusCode.Unauthorized, conElNuevo.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, conElViejo.StatusCode);
        }

        private Usuario ConfigurarRecepcion(out HttpClient cliente, out Guid proveedorId)
        {
            var usuario = _fabrica.CrearUsuario(RolUsuario.Recepcion);
            cliente = _fabrica.ClienteDe(usuario);
            var id = Guid.NewGuid();
            proveedorId = id;
            _fabrica.Proveedores.Setup(p => p.GetByIdAsync(id)).ReturnsAsync(Proveedor.Crear("Tambo Don Pedro", Guid.NewGuid()));
            return usuario;
        }

        private static object Recepcion(string? pin) => new
        {
            acidez = 17,
            temperatura = 5,
            litros = 1000,
            fechaHora = DateTime.Now.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm:ss"),
            ticket = 6000 + Random.Shared.Next(1, 1000),
            pin
        };

        [Fact]
        public async Task RegisterAsReception_WithoutPin_Is400OnThePinField()
        {
            // Arrange
            ConfigurarRecepcion(out var cliente, out var proveedorId);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Proveedores/{proveedorId}/recepciones", Recepcion(null), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal("pin", cuerpo.GetProperty("field").GetString());
        }

        [Fact]
        public async Task RegisterAsReception_WrongPin_Is400OnThePinField()
        {
            // Arrange
            ConfigurarRecepcion(out var cliente, out var proveedorId);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Proveedores/{proveedorId}/recepciones", Recepcion("0000"), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal("PIN incorrecto.", cuerpo.GetProperty("error").GetString());
        }

        [Fact]
        public async Task RegisterAsReception_FiveWrongPins_Answers429AndKeepsRefusingTheRightOne()
        {
            // Arrange
            ConfigurarRecepcion(out var cliente, out var proveedorId);
            var ruta = $"/api/Proveedores/{proveedorId}/recepciones";
            for (var i = 0; i < Usuario.MaxFallosPin - 1; i++)
            {
                var intento = await cliente.PostAsJsonAsync(ruta, Recepcion("0000"), TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.BadRequest, intento.StatusCode);
            }

            // Act
            var quinto = await cliente.PostAsJsonAsync(ruta, Recepcion("0000"), TestContext.Current.CancellationToken);
            var conElCorrecto = await cliente.PostAsJsonAsync(ruta, Recepcion("1234"), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.TooManyRequests, quinto.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, conElCorrecto.StatusCode);
            var cuerpo = await conElCorrecto.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Contains("PIN", cuerpo.GetProperty("error").GetString());
        }

        [Fact]
        public async Task RegisterAsReception_RightPin_StampsTheUserTheOperarioAndAnswersWithTheirName()
        {
            // Arrange
            var usuario = ConfigurarRecepcion(out var cliente, out var proveedorId);
            var hasher = new PinHasherHmac(Microsoft.Extensions.Options.Options.Create(new DairyControl.Application.Settings.PinSettings { Secret = FabricaDeApi.ClavePin }));
            var operario = Operario.Crear("Juan Pérez", hasher.Hash("4321"));
            _fabrica.Operarios.Setup(o => o.GetActivoPorPinHashAsync(operario.PinHash)).ReturnsAsync(operario);
            RecepcionLeche? guardada = null;
            _fabrica.Recepciones.Setup(r => r.AddAsync(It.IsAny<RecepcionLeche>()))
                .Callback<RecepcionLeche>(r => guardada = r).Returns(Task.CompletedTask);

            // Act
            var respuesta = await cliente.PostAsJsonAsync($"/api/Proveedores/{proveedorId}/recepciones", Recepcion("4321"), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal("Juan Pérez", cuerpo.GetProperty("operarioNombre").GetString());
            Assert.Equal("Tambo Don Pedro", cuerpo.GetProperty("proveedorNombre").GetString());
            Assert.NotNull(guardada);
            Assert.Equal(usuario.Id, guardada.RegistradoPorUsuarioId); // from the token, not from the request
            Assert.Equal(operario.Id, guardada.OperarioId);
        }
    }
}
