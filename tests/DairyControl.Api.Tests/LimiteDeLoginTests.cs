using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DairyControl.Api.Tests
{
    // The per-address limit on /api/Auth/login. The window is one minute, so a test of its own uses an app of its own.
    public class LimiteDeLoginTests : IClassFixture<LimiteDeLoginTests.ApiConLimiteBajo>
    {
        public sealed class ApiConLimiteBajo : FabricaDeApi
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.UseSetting("RateLimit:LoginPorMinuto", "3");
            }
        }

        private readonly ApiConLimiteBajo _fabrica;

        public LimiteDeLoginTests(ApiConLimiteBajo fabrica)
        {
            _fabrica = fabrica;
        }

        private static async Task<HttpResponseMessage> Intentar(HttpClient cliente, string? xForwardedFor = null)
        {
            var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/login")
            {
                Content = JsonContent.Create(new { username = "nadie", password = "nada" })
            };
            if (xForwardedFor is not null)
            {
                pedido.Headers.Add("X-Forwarded-For", xForwardedFor);
            }
            return await cliente.SendAsync(pedido, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task Login_MoreAttemptsThanTheLimit_Answers429WithAMessage()
        {
            // Arrange
            var cliente = _fabrica.CreateClient();
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await Intentar(cliente)).StatusCode);
            }

            // Act
            var respuesta = await Intentar(cliente);

            // Assert
            Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Contains("Demasiados intentos", cuerpo.GetProperty("error").GetString());
        }
    }

    public class LimiteDeLoginConCabeceraFalsaTests : IClassFixture<LimiteDeLoginConCabeceraFalsaTests.ApiConLimiteBajo>
    {
        public sealed class ApiConLimiteBajo : FabricaDeApi
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.UseSetting("RateLimit:LoginPorMinuto", "3");
            }
        }

        private readonly ApiConLimiteBajo _fabrica;

        public LimiteDeLoginConCabeceraFalsaTests(ApiConLimiteBajo fabrica)
        {
            _fabrica = fabrica;
        }

        [Fact]
        public async Task Login_ChangingTheClientSuppliedForwardedForEveryTime_DoesNotGiveAFreshBucket()
        {
            // Arrange: the proxy appends the address it saw after whatever the client sent, so the client can only invent the entries
            // before it (a different one each time here); the last entry is always the same
            var cliente = _fabrica.CreateClient();
            var estados = new List<HttpStatusCode>();

            // Act
            for (var i = 0; i < 6; i++)
            {
                var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/login")
                {
                    Content = JsonContent.Create(new { username = "nadie", password = "nada" })
                };
                pedido.Headers.Add("X-Forwarded-For", $"203.0.113.{i + 1}, 198.51.100.7");
                estados.Add((await cliente.SendAsync(pedido, TestContext.Current.CancellationToken)).StatusCode);
            }

            // Assert
            Assert.Equal(3, estados.Count(e => e == HttpStatusCode.Unauthorized));
            Assert.Equal(3, estados.Count(e => e == HttpStatusCode.TooManyRequests));
        }
    }
}
