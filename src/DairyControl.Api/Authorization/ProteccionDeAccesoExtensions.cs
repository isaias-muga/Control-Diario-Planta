using DairyControl.Api.Validation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace DairyControl.Api.Authorization
{
    /// <summary>The limit on login attempts per client address, and the way the client address is read behind the proxy.</summary>
    public static class ProteccionDeAccesoExtensions
    {
        public const string PoliticaLogin = "login";
        public const int LoginPorMinutoPredeterminado = 30;

        // The proxy (nginx, the cloud ingress) lives in a private network; these are the networks trusted to send X-Forwarded-For
        private static readonly string[] RedesDeConfianzaPredeterminadas =
            ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];

        /// <summary>
        /// The client address is the last hop the trusted proxy saw (ForwardLimit 1): whatever the client puts in its own
        /// X-Forwarded-For comes first in the list and is ignored, so it cannot be used to get a fresh rate-limit bucket.
        /// </summary>
        public static IServiceCollection AddDairyControlProteccionDeAcceso(this IServiceCollection services)
        {
            services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((opciones, configuracion) =>
            {
                opciones.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                opciones.ForwardLimit = configuracion.GetValue("Proxy:ForwardLimit", 1);
                opciones.KnownProxies.Clear();
                opciones.KnownIPNetworks.Clear();
                var redes = configuracion.GetSection("Proxy:KnownNetworks").Get<string[]>() is { Length: > 0 } configuradas
                    ? configuradas
                    : RedesDeConfianzaPredeterminadas;
                foreach (var red in redes)
                {
                    opciones.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(red));
                }
            });

            services.AddRateLimiter(opciones =>
            {
                opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                opciones.OnRejected = async (contexto, cancelacion) =>
                {
                    contexto.HttpContext.Response.ContentType = "application/json";
                    await contexto.HttpContext.Response.WriteAsync(
                        JsonSerializer.Serialize(ApiErrors.Body("Demasiados intentos. Probá de nuevo en un minuto.")), cancelacion);
                };
                opciones.AddPolicy(PoliticaLogin, contexto =>
                {
                    var limite = contexto.RequestServices.GetRequiredService<IConfiguration>()
                        .GetValue("RateLimit:LoginPorMinuto", LoginPorMinutoPredeterminado);
                    return RateLimitPartition.GetFixedWindowLimiter(
                        contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = limite, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
                });
            });
            return services;
        }
    }
}
