using DairyControl.Api.Authorization;
using DairyControl.Application.DTOs;
using DairyControl.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DairyControl.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProveedoresController : ControllerBase
    {
        private readonly ProveedorAppService _service;
        private readonly ProveedorCambiosAppService _cambios;

        public ProveedoresController(ProveedorAppService service, ProveedorCambiosAppService cambios)
        {
            _service = service;
            _cambios = cambios;
        }

        [HttpPost]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<ActionResult<ProveedorDto>> CrearAsync([FromBody] CrearProveedorDto dto)
        {
            var proveedor = await _service.CrearAsync(dto);
            return CreatedAtRoute("GetProveedorById", new { id = proveedor.Id }, proveedor);
        }

        [HttpGet]
        [Authorize(Policy = Politicas.Lectura)]
        public async Task<ActionResult<IEnumerable<ProveedorDto>>> GetAllAsync()
        {
            var proveedores = await _service.GetAllAsync();
            return Ok(proveedores);
        }

        // Id and name only: this is what the reception phone may read
        [HttpGet("opciones")]
        [Authorize(Policy = Politicas.LecturaCarga)]
        public async Task<ActionResult<IEnumerable<ProveedorOpcionDto>>> GetOpcionesAsync()
        {
            return Ok(await _service.GetOpcionesAsync());
        }

        [HttpGet("{id}", Name = "GetProveedorById")]
        [Authorize(Policy = Politicas.Lectura)]
        public async Task<ActionResult<ProveedorDto>> GetByIdAsync(Guid id)
        {
            var proveedor = await _service.GetByIdAsync(id);
            if (proveedor == null)
            {
                return NotFound();
            }
            return Ok(proveedor);
        }

        // Correct the name, with a reason (the old name stays in the history)
        [HttpPut("{id}")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<ActionResult<ProveedorDto>> RenombrarAsync(Guid id, [FromBody] RenombrarProveedorDto dto)
        {
            var proveedor = await _cambios.RenombrarAsync(id, dto);
            return proveedor is null ? NotFound() : Ok(proveedor);
        }

        // A proveedor is never deleted: it is deactivated (not offered for new recepciones, all history kept) and can be reactivated
        [HttpPost("{id}/desactivar")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<IActionResult> DesactivarAsync(Guid id, [FromBody] MotivoCambioDto dto)
        {
            return await _cambios.DesactivarAsync(id, dto) ? NoContent() : NotFound();
        }

        [HttpPost("{id}/reactivar")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<IActionResult> ReactivarAsync(Guid id, [FromBody] MotivoCambioDto dto)
        {
            return await _cambios.ReactivarAsync(id, dto) ? NoContent() : NotFound();
        }

        [HttpGet("{id}/historial")]
        [Authorize(Policy = Politicas.Lectura)]
        public async Task<ActionResult<IEnumerable<CorreccionDto>>> GetHistorialAsync(Guid id)
        {
            var historial = await _cambios.GetHistorialAsync(id);
            return historial is null ? NotFound() : Ok(historial);
        }

        [HttpPost("{id}/recepciones")]
        [Authorize(Policy = Politicas.Registro)]
        public async Task<ActionResult<RecepcionRegistradaDto>> RegistrarRecepcionAsync(Guid id, [FromBody] RegistrarRecepcionDto dto)
        {
            var recepcion = await _service.RegistrarRecepcionAsync(id, dto);
            if (recepcion == null)
            {
                return NotFound();
            }
            return Ok(recepcion);
        }
    }
}