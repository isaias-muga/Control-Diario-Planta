using Microsoft.AspNetCore.Authorization;

namespace DairyControl.Api.Authorization
{
    /// <summary>
    /// Fails while the user must choose a new password (after being created or reset by an admin). It is part of every policy except
    /// <see cref="Politicas.CambioPassword"/>, so the server refuses everything else until the password is changed.
    /// </summary>
    public sealed class PasswordVigenteRequirement : IAuthorizationRequirement
    {
        /// <summary>
        /// Added to the principal from the database when the token is validated. The token also carries a "dcp" claim, but it is only a
        /// hint for the client; the server never reads it.
        /// </summary>
        public const string ClaimDebeCambiar = "srv:debe-cambiar-password";
    }

    public sealed class PasswordVigenteHandler : AuthorizationHandler<PasswordVigenteRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PasswordVigenteRequirement requirement)
        {
            if (!context.User.HasClaim(c => c.Type == PasswordVigenteRequirement.ClaimDebeCambiar))
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }
}
