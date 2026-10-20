using DairyControl.Api.Authorization;
using DairyControl.Application.DTOs;
using DairyControl.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DairyControl.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsuariosController : ControllerBase
    {
        private readonly UsuarioAppService _service;

        public UsuariosController(UsuarioAppService service)
        {
            _service = service;
        }

        [HttpGet]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<ActionResult<IEnumerable<UsuarioDto>>> GetAllAsync() => Ok(await _service.GetAllAsync());

        [HttpPost]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<ActionResult<UsuarioDto>> CrearAsync([FromBody] CrearUsuarioDto dto) => Ok(await _service.CrearAsync(dto));

        [HttpPost("{id}/desactivar")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<IActionResult> DesactivarAsync(Guid id, [FromBody] MotivoCambioDto dto) =>
            await _service.DesactivarAsync(id, dto) ? NoContent() : NotFound();

        [HttpPost("{id}/reactivar")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<IActionResult> ReactivarAsync(Guid id, [FromBody] MotivoCambioDto dto) =>
            await _service.ReactivarAsync(id, dto) ? NoContent() : NotFound();

        [HttpPut("{id}/rol")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<ActionResult<UsuarioDto>> CambiarRolAsync(Guid id, [FromBody] CambiarRolDto dto)
        {
            var usuario = await _service.CambiarRolAsync(id, dto);
            return usuario is null ? NotFound() : Ok(usuario);
        }

        [HttpGet("{id}/historial")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<ActionResult<IEnumerable<CorreccionDto>>> GetHistorialAsync(Guid id)
        {
            var historial = await _service.GetHistorialAsync(id);
            return historial is null ? NotFound() : Ok(historial);
        }

        [HttpPost("{id}/restablecer-password")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<IActionResult> RestablecerPasswordAsync(Guid id, [FromBody] RestablecerPasswordDto dto) =>
            await _service.RestablecerPasswordAsync(id, dto) ? NoContent() : NotFound();
    }
}
