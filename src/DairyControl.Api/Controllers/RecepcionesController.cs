using DairyControl.Api.Authorization;
using DairyControl.Application.DTOs;
using DairyControl.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DairyControl.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RecepcionesController : ControllerBase
    {
        private readonly ResumenRecepcionesAppService _service;
        private readonly ListadoRecepcionesAppService _listadoService;
        private readonly RecepcionAppService _recepciones;

        public RecepcionesController(ResumenRecepcionesAppService service, ListadoRecepcionesAppService listadoService, RecepcionAppService recepciones)
        {
            _service = service;
            _listadoService = listadoService;
            _recepciones = recepciones;
        }

        [HttpGet]
        [Authorize(Policy = Politicas.Lectura)]
        public async Task<ActionResult<ListadoRecepcionesDto>> GetListadoAsync(
            [FromQuery, BindRequired] DateOnly desde,
            [FromQuery, BindRequired] DateOnly hasta,
            [FromQuery] Guid? proveedorId,
            [FromQuery] int? ticket,
            [FromQuery] bool soloFueraDeRango = false,
            [FromQuery] bool incluirAnuladas = false,
            [FromQuery] int pagina = 1)
        {
            var listado = await _listadoService.GetListadoAsync(new FiltroRecepciones
            {
                Desde = desde,
                Hasta = hasta,
                ProveedorId = proveedorId,
                Ticket = ticket,
                SoloFueraDeRango = soloFueraDeRango,
                IncluirAnuladas = incluirAnuladas,
                Pagina = pagina
            });
            return Ok(listado);
        }

        [HttpGet("siguiente-ticket")]
        [Authorize(Policy = Politicas.LecturaCarga)]
        public async Task<ActionResult<SiguienteTicketDto>> GetSiguienteTicketAsync()
        {
            var ticket = await _listadoService.GetSiguienteTicketAsync();
            return Ok(new SiguienteTicketDto { Ticket = ticket });
        }

        // The recepción as the correction form needs it, with its version. Admin only: it is what a correction starts from.
        [HttpGet("{id:guid}")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<ActionResult<RecepcionDetalleDto>> GetDetalleAsync(Guid id)
        {
            var detalle = await _recepciones.GetDetalleAsync(id);
            return detalle is null ? NotFound() : Ok(detalle);
        }

        // Correct a recepción with a reason; the old values stay in its history. Never a delete.
        [HttpPut("{id:guid}")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<ActionResult<RecepcionDetalleDto>> CorregirAsync(Guid id, [FromBody] CorregirRecepcionDto dto)
        {
            var detalle = await _recepciones.CorregirAsync(id, dto);
            return detalle is null ? NotFound() : Ok(detalle);
        }

        // Void for good, with a reason: the recepción stays (and keeps its ticket used) but counts in nothing.
        [HttpPost("{id:guid}/anular")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<IActionResult> AnularAsync(Guid id, [FromBody] MotivoCambioDto dto)
        {
            return await _recepciones.AnularAsync(id, dto) ? NoContent() : NotFound();
        }

        [HttpGet("{id:guid}/historial")]
        [Authorize(Policy = Politicas.Lectura)]
        public async Task<ActionResult<IEnumerable<CorreccionDto>>> GetHistorialAsync(Guid id)
        {
            var historial = await _recepciones.GetHistorialAsync(id);
            return historial is null ? NotFound() : Ok(historial);
        }

        [HttpGet("resumen")]
        [Authorize(Policy = Politicas.Lectura)]
        public async Task<ActionResult<ResumenRecepcionesDto>> GetResumenAsync(
            [FromQuery, BindRequired] DateOnly desde,
            [FromQuery, BindRequired] DateOnly hasta,
            [FromQuery] Guid? proveedorId)
        {
            var resumen = await _service.GetResumenAsync(desde, hasta, proveedorId);
            return Ok(resumen);
        }
    }
}
