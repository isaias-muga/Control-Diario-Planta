using DairyControl.Application.DTOs;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Application.Services
{
    public class ConfiguracionCalidadAppService
    {
        private readonly IConfiguracionCalidadRepository _repository;
        private readonly TimeProvider _reloj;

        public ConfiguracionCalidadAppService(IConfiguracionCalidadRepository repository, TimeProvider reloj)
        {
            _repository = repository;
            _reloj = reloj;
        }

        public async Task<LimitesCalidadDto?> GetAsync()
        {
            var leida = await _repository.GetAsync();
            return leida is null ? null : ToDto(leida.Configuracion, leida.Version);
        }

        public async Task<LimitesCalidadDto?> ActualizarAsync(ActualizarLimitesCalidadDto dto)
        {
            var versionLeida = LeerVersion(dto.Version);
            var limites = LimitesCalidad.Create(dto.AcidezMaxima, dto.TemperaturaMaxima, dto.AguaMaxima);

            var actual = await _repository.GetAsync();
            if (actual is null)
                return null;

            actual.Configuracion.Actualizar(limites, dto.CantidadSilos, _reloj.GetUtcNow().UtcDateTime);
            var nuevaVersion = await _repository.UpdateAsync(actual.Configuracion, versionLeida);
            return ToDto(actual.Configuracion, nuevaVersion);
        }

        // The version tells the repository which state the user was looking at; without it a stale edit would go unnoticed.
        private static byte[] LeerVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                throw new ReglaDeNegocioException("Falta la versión de los límites. Recargá la página.", "version");

            var bytes = new byte[version.Length];
            if (!Convert.TryFromBase64String(version, bytes, out var escritos) || escritos == 0)
                throw new ReglaDeNegocioException("La versión de los límites no es válida. Recargá la página.", "version");

            return bytes[..escritos];
        }

        private static LimitesCalidadDto ToDto(ConfiguracionCalidad configuracion, byte[] version) => new()
        {
            AcidezMaxima = configuracion.Limites.AcidezMaxima,
            TemperaturaMaxima = configuracion.Limites.TemperaturaMaxima,
            AguaMaxima = configuracion.Limites.AguaMaxima,
            CantidadSilos = configuracion.CantidadSilos,
            ActualizadoEn = configuracion.ActualizadoEn,
            Version = Convert.ToBase64String(version)
        };
    }
}
