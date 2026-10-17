using DairyControl.Application.Services;
using DairyControl.Domain.Enums;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace DairyControl.Api.Authorization
{
    public static class AutenticacionExtensions
    {
        /// <summary>JWT bearer authentication. On every request the user must still exist, be active and carry the current security stamp.</summary>
        public static IServiceCollection AddDairyControlAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpContextAccessor();
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    // Keep the claim names as the token has them ("sub", "role"), not the long .NET ones
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = configuration["Jwt:Issuer"],
                        ValidateAudience = true,
                        ValidAudience = configuration["Jwt:Audience"],
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:SigningKey"]!)),
                        ValidateLifetime = true,
                        RoleClaimType = AuthService.ClaimRol,
                        NameClaimType = AuthService.ClaimNombre
                    };
                    options.Events = new JwtBearerEvents { OnTokenValidated = ValidarSesionAsync };
                });
            return services;
        }

        private static async Task ValidarSesionAsync(TokenValidatedContext contexto)
        {
            var principal = contexto.Principal;
            if (!Guid.TryParse(principal?.FindFirstValue("sub"), out var usuarioId) ||
                !Guid.TryParse(principal.FindFirstValue(AuthService.ClaimSello), out var sello))
            {
                contexto.Fail("El token no tiene un usuario válido.");
                return;
            }

            var sesion = await contexto.HttpContext.RequestServices.GetRequiredService<AuthService>().ValidarSesionAsync(usuarioId, sello);
            if (sesion is null)
            {
                contexto.Fail("La sesión terminó: el usuario fue desactivado o cambió su contraseña.");
                return;
            }

            // What the server decides comes from the database, never from the token
            var identidad = (ClaimsIdentity)principal.Identity!;
            foreach (var claim in identidad.FindAll(PasswordVigenteRequirement.ClaimDebeCambiar).ToList())
            {
                identidad.RemoveClaim(claim);
            }
            if (sesion.DebeCambiarPassword)
            {
                identidad.AddClaim(new Claim(PasswordVigenteRequirement.ClaimDebeCambiar, "1"));
            }
        }

        /// <summary>The policies: who may do what. The default and the fallback are "Admin or Oficina".</summary>
        public static IServiceCollection AddDairyControlAuthorization(this IServiceCollection services)
        {
            services.AddSingleton<IAuthorizationHandler, PasswordVigenteHandler>();
            services.AddSingleton<IAuthorizationMiddlewareResultHandler, ResultadoAutorizacion>();
            services.AddAuthorizationBuilder()
                .SetDefaultPolicy(Politica(RolUsuario.Admin, RolUsuario.Oficina))
                .SetFallbackPolicy(Politica(RolUsuario.Admin, RolUsuario.Oficina))
                .AddPolicy(Politicas.Lectura, Politica(RolUsuario.Admin, RolUsuario.Oficina))
                .AddPolicy(Politicas.LecturaCarga, Politica(RolUsuario.Admin, RolUsuario.Oficina, RolUsuario.Recepcion))
                .AddPolicy(Politicas.Registro, Politica(RolUsuario.Admin, RolUsuario.Oficina, RolUsuario.Recepcion))
                .AddPolicy(Politicas.Administracion, Politica(RolUsuario.Admin))
                .AddPolicy(Politicas.Autenticado, Politica(RolUsuario.Admin, RolUsuario.Oficina, RolUsuario.Recepcion))
                .AddPolicy(Politicas.CambioPassword, new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .RequireRole(nameof(RolUsuario.Admin), nameof(RolUsuario.Oficina), nameof(RolUsuario.Recepcion))
                    .Build());
            return services;
        }

        // Every policy but CambioPassword also requires that the password is not pending a change
        private static AuthorizationPolicy Politica(params RolUsuario[] roles) =>
            new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireRole(roles.Select(r => r.ToString()))
                .AddRequirements(new PasswordVigenteRequirement())
                .Build();
    }
}
