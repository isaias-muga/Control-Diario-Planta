using DairyControl.Api.Authorization;
using DairyControl.Api.Validation;
using DairyControl.Application.DTOs;
using DairyControl.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DairyControl.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;

        public AuthController(AuthService authService)
        {
            _authService = authService;
        }

        // The only anonymous endpoint besides /health. A wrong password, an unknown user, an inactive one and a locked one all answer the same.
        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting(ProteccionDeAccesoExtensions.PoliticaLogin)]
        public async Task<ActionResult<LoginResultadoDto>> LoginAsync([FromBody] LoginDto dto)
        {
            var resultado = await _authService.LoginAsync(dto);
            if (resultado == null)
            {
                return Unauthorized(ApiErrors.Body("Usuario o contraseña incorrectos."));
            }
            return Ok(resultado);
        }

        // Sliding session: the client asks for a fresh token before the current one expires
        [HttpPost("renovar")]
        [Authorize(Policy = Politicas.Autenticado)]
        public async Task<ActionResult<LoginResultadoDto>> RenovarAsync()
        {
            var resultado = await _authService.RenovarAsync();
            if (resultado == null)
            {
                return Unauthorized();
            }
            return Ok(resultado);
        }

        // Open to a user that must change the password first: it is the only thing that user may do
        [HttpPost("cambiar-password")]
        [Authorize(Policy = Politicas.CambioPassword)]
        public async Task<ActionResult<LoginResultadoDto>> CambiarPasswordAsync([FromBody] CambiarPasswordDto dto)
        {
            var resultado = await _authService.CambiarPasswordAsync(dto);
            if (resultado == null)
            {
                return Unauthorized();
            }
            return Ok(resultado);
        }
    }
}
