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

        public RecepcionesController(ResumenRecepcionesAppService service, ListadoRecepcionesAppService listadoService)
        {
            _service = service;
            _listadoService = listadoService;
        }

        [HttpGet]
        [Authorize(Policy = Politicas.Lectura)]
        public async Task<ActionResult<ListadoRecepcionesDto>> GetListadoAsync(
            [FromQuery, BindRequired] DateOnly desde,
            [FromQuery, BindRequired] DateOnly hasta,
            [FromQuery] Guid? proveedorId,
            [FromQuery] int? ticket,
            [FromQuery] bool soloFueraDeRango = false,
            [FromQuery] int pagina = 1)
        {
            var listado = await _listadoService.GetListadoAsync(new FiltroRecepciones
            {
                Desde = desde,
                Hasta = hasta,
                ProveedorId = proveedorId,
                Ticket = ticket,
                SoloFueraDeRango = soloFueraDeRango,
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
