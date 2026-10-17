using DairyControl.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using System.Text.Json;

namespace DairyControl.Api.Authorization
{
    /// <summary>Gives the 403 answers a JSON body: the reason is "change your password first" or "not allowed". A 401 stays the default challenge.</summary>
    public sealed class ResultadoAutorizacion : IAuthorizationMiddlewareResultHandler
    {
        public const string CodigoDebeCambiarPassword = "debeCambiarPassword";

        private readonly AuthorizationMiddlewareResultHandler _predeterminado = new();

        public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult resultado)
        {
            if (!resultado.Forbidden)
            {
                await _predeterminado.HandleAsync(next, context, policy, resultado);
                return;
            }

            var debeCambiar = resultado.AuthorizationFailure?.FailedRequirements.OfType<PasswordVigenteRequirement>().Any() == true;
            object cuerpo = debeCambiar
                ? new { error = "Tenés que cambiar la contraseña antes de continuar.", code = CodigoDebeCambiarPassword }
                : ApiErrors.Body("No tenés permiso para esta acción.");

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(cuerpo));
        }
    }
}
