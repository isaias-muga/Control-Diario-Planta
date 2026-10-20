using DairyControl.Api.Authorization;
using DairyControl.Application.DTOs;
using DairyControl.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DairyControl.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OperariosController : ControllerBase
    {
        private readonly OperarioAppService _service;

        public OperariosController(OperarioAppService service)
        {
            _service = service;
        }

        [HttpGet]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<ActionResult<IEnumerable<OperarioDto>>> GetAllAsync() => Ok(await _service.GetAllAsync());

        [HttpPost]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<ActionResult<OperarioDto>> CrearAsync([FromBody] CrearOperarioDto dto) => Ok(await _service.CrearAsync(dto));

        [HttpPost("{id}/desactivar")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<IActionResult> DesactivarAsync(Guid id, [FromBody] MotivoCambioDto dto) =>
            await _service.DesactivarAsync(id, dto) ? NoContent() : NotFound();

        [HttpPost("{id}/reactivar")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<IActionResult> ReactivarAsync(Guid id, [FromBody] MotivoCambioDto dto) =>
            await _service.ReactivarAsync(id, dto) ? NoContent() : NotFound();

        [HttpGet("{id}/historial")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<ActionResult<IEnumerable<CorreccionDto>>> GetHistorialAsync(Guid id)
        {
            var historial = await _service.GetHistorialAsync(id);
            return historial is null ? NotFound() : Ok(historial);
        }

        [HttpPut("{id}/pin")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<IActionResult> CambiarPinAsync(Guid id, [FromBody] CambiarPinDto dto) =>
            await _service.CambiarPinAsync(id, dto) ? NoContent() : NotFound();
    }
}
