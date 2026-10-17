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

        public ProveedoresController(ProveedorAppService service)
        {
            _service = service;
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