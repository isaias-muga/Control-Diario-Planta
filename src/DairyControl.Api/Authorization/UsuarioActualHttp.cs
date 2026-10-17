using DairyControl.Application.Seguridad;
using DairyControl.Application.Services;
using DairyControl.Domain.Enums;
using System.Security.Claims;

namespace DairyControl.Api.Authorization
{
    /// <summary>The current user, read from the validated token of the request.</summary>
    public sealed class UsuarioActualHttp : IUsuarioActual
    {
        private readonly IHttpContextAccessor _accessor;

        public UsuarioActualHttp(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

        public Guid? Id => Guid.TryParse(Principal?.FindFirstValue("sub"), out var id) ? id : null;

        public RolUsuario? Rol => Enum.TryParse<RolUsuario>(Principal?.FindFirstValue(AuthService.ClaimRol), out var rol) && Enum.IsDefined(rol) ? rol : null;
    }
}
