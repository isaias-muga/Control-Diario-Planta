using DairyControl.Api.Authorization;
using DairyControl.Application.DTOs;
using DairyControl.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DairyControl.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConfiguracionController : ControllerBase
    {
        private readonly ConfiguracionCalidadAppService _service;

        public ConfiguracionController(ConfiguracionCalidadAppService service)
        {
            _service = service;
        }

        [HttpGet("calidad")]
        [Authorize(Policy = Politicas.LecturaCarga)]
        public async Task<ActionResult<LimitesCalidadDto>> GetCalidadAsync()
        {
            var limites = await _service.GetAsync();
            if (limites is null)
                return NotFound();

            return Ok(limites);
        }

        [HttpPut("calidad")]
        [Authorize(Policy = Politicas.Administracion)]
        public async Task<ActionResult<LimitesCalidadDto>> ActualizarCalidadAsync([FromBody] ActualizarLimitesCalidadDto dto)
        {
            var limites = await _service.ActualizarAsync(dto);
            if (limites is null)
                return NotFound();

            return Ok(limites);
        }
    }
}
