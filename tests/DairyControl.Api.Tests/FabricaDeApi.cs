using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using DairyControl.Application.Queries;
using DairyControl.Application.Seguridad;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Domain.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace DairyControl.Api.Tests
{
    /// <summary>
    /// The real application in memory (real pipeline, real authentication and authorization, real application services) with every
    /// repository and query replaced by a mock: no database. The users the tests log in as live in the mocked user repository.
    /// </summary>
    public class FabricaDeApi : WebApplicationFactory<Program>
    {
        public const string ClaveJwt = "una-clave-de-prueba-de-al-menos-32-caracteres";
        public const string ClavePin = "un-secreto-de-pin-de-prueba-de-32-caracteres";

        static FabricaDeApi()
        {
            // The configuration the real Program reads while it registers its services
            Environment.SetEnvironmentVariable("Jwt__SigningKey", ClaveJwt);
            Environment.SetEnvironmentVariable("Pin__Secret", ClavePin);
            Environment.SetEnvironmentVariable("HttpsRedirection__Enabled", "false");
        }

        public Mock<IUsuarioRepository> Usuarios { get; } = new();
        public Mock<IOperarioRepository> Operarios { get; } = new();
        public Mock<IProveedorRepository> Proveedores { get; } = new();
        public Mock<IRecepcionRepository> Recepciones { get; } = new();

        public FabricaDeApi()
        {
            // Lists come back empty instead of null, so an allowed call does not fail inside the service
            Usuarios.Setup(u => u.GetAllAsync()).ReturnsAsync([]);
            Operarios.Setup(o => o.GetAllAsync()).ReturnsAsync([]);
            Proveedores.Setup(p => p.GetAllAsync()).ReturnsAsync([]);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); // not Development: that would load the user secrets (the real admin password)
            builder.ConfigureTestServices(servicios =>
            {
                Reemplazar(servicios, Usuarios.Object);
                Reemplazar(servicios, Operarios.Object);
                Reemplazar(servicios, Proveedores.Object);
                Reemplazar(servicios, Recepciones.Object);
                Reemplazar(servicios, new Mock<IProveedorQueries>().Object);
                Reemplazar(servicios, new Mock<IRecepcionQueries>().Object);
                Reemplazar(servicios, new Mock<IConfiguracionCalidadRepository>().Object);

                // /health asks the database: here there is none
                servicios.PostConfigure<HealthCheckServiceOptions>(o => o.Registrations.Clear());

                // The test server has no client address: act as a client that reaches the proxy from the loopback
                servicios.AddTransient<IStartupFilter, DireccionDeOrigenDeLaPrueba>();
            });
        }

        private static void Reemplazar<T>(IServiceCollection servicios, T instancia) where T : class
        {
            servicios.RemoveAll<T>();
            servicios.AddSingleton(instancia);
        }

        /// <summary>A user stored in the mocked repository (active, password already chosen unless asked otherwise).</summary>
        public Usuario CrearUsuario(RolUsuario rol, bool debeCambiarPassword = false, string hash = "hash", string? username = null)
        {
            var usuario = Usuario.Crear(username ?? "u" + Guid.NewGuid().ToString("N")[..10], "Persona de prueba", rol, hash, debeCambiarPassword);
            Usuarios.Setup(u => u.GetByIdAsync(usuario.Id)).ReturnsAsync(usuario);
            Usuarios.Setup(u => u.GetByUsernameAsync(usuario.Username)).ReturnsAsync(usuario);
            return usuario;
        }

        /// <summary>A client that sends a token of the user. The token carries the stamp the user has now.</summary>
        public HttpClient ClienteDe(Usuario usuario, params Claim[] extra)
        {
            var cliente = CreateClient();
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(usuario, extra));
            return cliente;
        }

        public HttpClient ClienteDe(RolUsuario rol) => ClienteDe(CrearUsuario(rol));

        public static string Token(Usuario usuario, params Claim[] extra)
        {
            var credenciales = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ClaveJwt)), SecurityAlgorithms.HmacSha256);
            var descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(
                [
                    new Claim("sub", usuario.Id.ToString()),
                    new Claim("role", usuario.Rol.ToString()),
                    new Claim("stamp", usuario.SecurityStamp.ToString()),
                    new Claim("name", usuario.Nombre),
                    .. extra
                ]),
                Expires = DateTime.UtcNow.AddHours(1),
                Issuer = "DairyControl.Api",
                Audience = "DairyControl.Api",
                SigningCredentials = credenciales
            };
            var handler = new JwtSecurityTokenHandler();
            return handler.WriteToken(handler.CreateToken(descriptor));
        }

        private sealed class DireccionDeOrigenDeLaPrueba : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> siguiente) =>
                app =>
                {
                    app.Use(async (contexto, next) =>
                    {
                        contexto.Connection.RemoteIpAddress = IPAddress.Loopback;
                        await next();
                    });
                    siguiente(app);
                };
        }
    }
}
