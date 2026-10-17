using System.Net;
using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
using DairyControl.Domain.Enums;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace DairyControl.Api.Tests
{
    // The real pipeline over the real application with mocked persistence: who may call what, decided on the server.
    public class AutorizacionTests : IClassFixture<FabricaDeApi>
    {
        private readonly FabricaDeApi _fabrica;

        public AutorizacionTests(FabricaDeApi fabrica)
        {
            _fabrica = fabrica;
        }

        private static readonly Guid UnId = Guid.NewGuid();

        // Method, path, the roles that may call it. Anything else is 403; without a token it is 401.
        // The reply of an allowed call may be anything but 401/403 (the data behind it is mocked): only authorization is under test.
        private static readonly (string Metodo, string Ruta, RolUsuario[] Roles)[] Filas =
        [
            ("GET", "/api/Proveedores", [RolUsuario.Admin, RolUsuario.Oficina]),
            ("GET", "/api/Proveedores/opciones", [RolUsuario.Admin, RolUsuario.Oficina, RolUsuario.Recepcion]),
            ("GET", $"/api/Proveedores/{UnId}", [RolUsuario.Admin, RolUsuario.Oficina]),
            ("POST", "/api/Proveedores", [RolUsuario.Admin]),
            ("POST", $"/api/Proveedores/{UnId}/recepciones", [RolUsuario.Admin, RolUsuario.Oficina, RolUsuario.Recepcion]),
            ("GET", "/api/Recepciones?desde=2026-10-01&hasta=2026-10-02", [RolUsuario.Admin, RolUsuario.Oficina]),
            ("GET", "/api/Recepciones/resumen?desde=2026-10-01&hasta=2026-10-02", [RolUsuario.Admin, RolUsuario.Oficina]),
            ("GET", "/api/Recepciones/siguiente-ticket", [RolUsuario.Admin, RolUsuario.Oficina, RolUsuario.Recepcion]),
            ("GET", "/api/Configuracion/calidad", [RolUsuario.Admin, RolUsuario.Oficina, RolUsuario.Recepcion]),
            ("PUT", "/api/Configuracion/calidad", [RolUsuario.Admin]),
            ("GET", "/api/Usuarios", [RolUsuario.Admin]),
            ("POST", "/api/Usuarios", [RolUsuario.Admin]),
            ("POST", $"/api/Usuarios/{UnId}/desactivar", [RolUsuario.Admin]),
            ("POST", $"/api/Usuarios/{UnId}/restablecer-password", [RolUsuario.Admin]),
            ("GET", "/api/Operarios", [RolUsuario.Admin]),
            ("POST", "/api/Operarios", [RolUsuario.Admin]),
            ("POST", $"/api/Operarios/{UnId}/desactivar", [RolUsuario.Admin]),
            ("PUT", $"/api/Operarios/{UnId}/pin", [RolUsuario.Admin]),
            ("POST", "/api/Auth/renovar", [RolUsuario.Admin, RolUsuario.Oficina, RolUsuario.Recepcion]),
            ("POST", "/api/Auth/cambiar-password", [RolUsuario.Admin, RolUsuario.Oficina, RolUsuario.Recepcion]),
        ];


        public static TheoryData<string, string, RolUsuario[]> Matriz
        {
            get
            {
                var datos = new TheoryData<string, string, RolUsuario[]>();
                foreach (var (metodo, ruta, roles) in Filas)
                {
                    datos.Add(metodo, ruta, roles);
                }
                return datos;
            }
        }

        // "GET /api/Proveedores/3f2c...?x=1" -> "GET api/Proveedores/{id}" (the shape the router has)
        private static string Forma(string metodo, string ruta) =>
            metodo + " " + System.Text.RegularExpressions.Regex.Replace(ruta.Split('?')[0].TrimStart('/'), "[0-9a-f]{8}-[0-9a-f-]{27}", "{id}");

        [Fact]
        public void Matrix_CoversEveryEndpointOfTheControllers()
        {
            // Arrange: every controller endpoint the application really maps (the anonymous login is tested apart)
            var mapeados = _fabrica.Services.GetServices<EndpointDataSource>().SelectMany(f => f.Endpoints).OfType<RouteEndpoint>()
                .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>() is not null)
                .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
                .SelectMany(e => e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Select(m => m + " " + e.RoutePattern.RawText))
                .ToList();

            // Act
            var enLaMatriz = Filas.Select(f => Forma(f.Metodo, f.Ruta)).ToHashSet();

            // Assert: a new action that is not in the matrix fails here, and so does a row for an endpoint that no longer exists
            Assert.True(mapeados.Count >= 19, "Expected the API endpoints, found " + mapeados.Count);
            Assert.All(mapeados, m => Assert.Contains(m, enLaMatriz));
            Assert.All(enLaMatriz, m => Assert.Contains(m, mapeados));
        }

        private static HttpRequestMessage Pedido(string metodo, string ruta) => new(new HttpMethod(metodo), ruta)
        {
            Content = metodo == "GET" ? null : JsonContent.Create(new { })
        };

        [Theory]
        [MemberData(nameof(Matriz))]
        public async Task Endpoint_ByRole_AllowsOnlyTheRolesOfTheMatrix(string metodo, string ruta, RolUsuario[] permitidos)
        {
            foreach (var rol in Enum.GetValues<RolUsuario>())
            {
                // Arrange
                var cliente = _fabrica.ClienteDe(rol);

                // Act
                var respuesta = await cliente.SendAsync(Pedido(metodo, ruta), TestContext.Current.CancellationToken);

                // Assert
                if (permitidos.Contains(rol))
                {
                    Assert.True(respuesta.StatusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden,
                        $"{rol} should reach {metodo} {ruta} but got {(int)respuesta.StatusCode}");
                }
                else
                {
                    Assert.True(respuesta.StatusCode == HttpStatusCode.Forbidden, $"{rol} on {metodo} {ruta} should be 403 but was {(int)respuesta.StatusCode}");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Matriz))]
        public async Task Endpoint_WithoutToken_IsUnauthorized(string metodo, string ruta, RolUsuario[] permitidos)
        {
            Assert.NotEmpty(permitidos);

            // Act
            var respuesta = await _fabrica.CreateClient().SendAsync(Pedido(metodo, ruta), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        }

        [Fact]
        public async Task Health_WithoutToken_Returns200()
        {
            // Act
            var respuesta = await _fabrica.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        }

        [Fact]
        public void AnonymousEndpoints_AreExactlyLoginAndHealth()
        {
            // Arrange
            var fuentes = _fabrica.Services.GetServices<EndpointDataSource>();

            // Act
            var anonimos = fuentes.SelectMany(f => f.Endpoints).OfType<RouteEndpoint>()
                .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                .Select(e => e.RoutePattern.RawText)
                .OrderBy(t => t)
                .ToList();

            // Assert
            Assert.Equal(["/health", "api/Auth/login"], anonimos);
        }

        [Fact]
        public async Task Forbidden_HasAJsonBodyWithAMessage()
        {
            // Arrange
            var cliente = _fabrica.ClienteDe(RolUsuario.Oficina);

            // Act
            var respuesta = await cliente.PostAsJsonAsync("/api/Proveedores", new { nombre = "X" }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal("No tenés permiso para esta acción.", cuerpo.GetProperty("error").GetString());
        }

        [Fact]
        public async Task Token_OfADeactivatedUser_IsRejectedAtOnce()
        {
            // Arrange
            var usuario = _fabrica.CrearUsuario(RolUsuario.Admin);
            var cliente = _fabrica.ClienteDe(usuario);
            var antes = await cliente.GetAsync("/api/Proveedores/opciones", TestContext.Current.CancellationToken);

            // Act: the same token, after the admin deactivated the user
            usuario.Desactivar();
            var despues = await cliente.GetAsync("/api/Proveedores/opciones", TestContext.Current.CancellationToken);

            // Assert
            Assert.NotEqual(HttpStatusCode.Unauthorized, antes.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, despues.StatusCode);
        }

        [Fact]
        public async Task Token_AfterAPasswordChange_IsRejectedAtOnce()
        {
            // Arrange
            var usuario = _fabrica.CrearUsuario(RolUsuario.Oficina);
            var cliente = _fabrica.ClienteDe(usuario);

            // Act: the stamp changes when the password does
            usuario.CambiarPassword("otro-hash");
            var respuesta = await cliente.GetAsync("/api/Proveedores", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        }

        [Fact]
        public async Task Token_AfterARoleChange_IsRejectedAtOnce()
        {
            // Arrange
            var usuario = _fabrica.CrearUsuario(RolUsuario.Admin);
            var cliente = _fabrica.ClienteDe(usuario);

            // Act
            usuario.CambiarRol(RolUsuario.Oficina);
            var respuesta = await cliente.GetAsync("/api/Usuarios", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        }

        [Fact]
        public async Task Token_OfAnUnknownUser_IsRejected()
        {
            // Arrange: a valid signature for a user the database does not have
            var fantasma = Domain.Entities.Usuario.Crear("fantasma", "Fantasma", RolUsuario.Admin, "hash");
            var cliente = _fabrica.ClienteDe(fantasma);

            // Act
            var respuesta = await cliente.GetAsync("/api/Proveedores", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        }

        [Fact]
        public async Task Token_WithAnotherSigningKey_IsRejected()
        {
            // Arrange
            var cliente = _fabrica.CreateClient();
            cliente.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "eyJhbGciOiJIUzI1NiJ9.e30.firma-falsa");

            // Act
            var respuesta = await cliente.GetAsync("/api/Proveedores", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        }

        [Theory]
        [InlineData("GET", "/api/Proveedores")]
        [InlineData("GET", "/api/Proveedores/opciones")]
        [InlineData("GET", "/api/Configuracion/calidad")]
        [InlineData("POST", "/api/Auth/renovar")]
        [InlineData("POST", "/api/Usuarios")]
        public async Task UserWhoMustChangeThePassword_IsRefusedEverywhereButChangePassword(string metodo, string ruta)
        {
            // Arrange
            var usuario = _fabrica.CrearUsuario(RolUsuario.Admin, debeCambiarPassword: true);
            var cliente = _fabrica.ClienteDe(usuario);

            // Act
            var respuesta = await cliente.SendAsync(Pedido(metodo, ruta), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal("debeCambiarPassword", cuerpo.GetProperty("code").GetString());
        }

        [Fact]
        public async Task UserWhoMustChangeThePassword_CanReachChangePassword()
        {
            // Arrange
            var usuario = _fabrica.CrearUsuario(RolUsuario.Recepcion, debeCambiarPassword: true);
            var cliente = _fabrica.ClienteDe(usuario);

            // Act
            var respuesta = await cliente.PostAsJsonAsync("/api/Auth/cambiar-password",
                new { passwordActual = "mala", passwordNueva = "una-clave-nueva" }, TestContext.Current.CancellationToken);

            // Assert: it is answered by the endpoint (the current password is wrong), not refused by the policy
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        }

        [Fact]
        public async Task TokenClaimThatSaysMustChange_IsIgnoredByTheServer()
        {
            // Arrange: the user does NOT have to change the password, whatever a token might claim
            var usuario = _fabrica.CrearUsuario(RolUsuario.Oficina, debeCambiarPassword: false);
            var cliente = _fabrica.ClienteDe(usuario, new Claim("dcp", "1"), new Claim("srv:debe-cambiar-password", "1"));

            // Act
            var respuesta = await cliente.GetAsync("/api/Proveedores", TestContext.Current.CancellationToken);

            // Assert
            Assert.NotEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }
    }
}
