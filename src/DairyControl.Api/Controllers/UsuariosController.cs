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
        public async Task<IActionResult> DesactivarAsync(Guid id) => await _service.DesactivarAsync(id) ? NoContent() : NotFound();

        [HttpPost("{id}/restablecer-password")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<IActionResult> RestablecerPasswordAsync(Guid id, [FromBody] RestablecerPasswordDto dto) =>
            await _service.RestablecerPasswordAsync(id, dto) ? NoContent() : NotFound();
    }
}
