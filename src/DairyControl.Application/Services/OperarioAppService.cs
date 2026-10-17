using DairyControl.Application.DTOs;
using DairyControl.Application.Seguridad;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;

namespace DairyControl.Application.Services
{
    public class OperarioAppService
    {
        private readonly IOperarioRepository _operarios;
        private readonly IHasherDePin _hasher;

        public OperarioAppService(IOperarioRepository operarios, IHasherDePin hasher)
        {
            _operarios = operarios;
            _hasher = hasher;
        }

        public async Task<IReadOnlyList<OperarioDto>> GetAllAsync()
        {
            var operarios = await _operarios.GetAllAsync();
            return operarios.OrderBy(o => o.Nombre, StringComparer.CurrentCultureIgnoreCase).Select(ADto).ToList();
        }

        public async Task<OperarioDto> CrearAsync(CrearOperarioDto dto)
        {
            Operario.ValidarPin(dto.Pin);
            var pinHash = _hasher.Hash(dto.Pin);
            await ValidarPinLibreAsync(pinHash);

            var operario = Operario.Crear(dto.Nombre, pinHash);
            await _operarios.AddAsync(operario);
            return ADto(operario);
        }

        /// <summary>Returns false when the operario does not exist.</summary>
        public async Task<bool> DesactivarAsync(Guid id)
        {
            var operario = await _operarios.GetByIdAsync(id);
            if (operario is null)
            {
                return false;
            }
            operario.Desactivar();
            await _operarios.UpdateAsync(operario);
            return true;
        }

        /// <summary>Returns false when the operario does not exist.</summary>
        public async Task<bool> CambiarPinAsync(Guid id, CambiarPinDto dto)
        {
            var operario = await _operarios.GetByIdAsync(id);
            if (operario is null)
            {
                return false;
            }
            if (!operario.Activo)
            {
                throw new ReglaDeNegocioException("El operario está desactivado.", "id");
            }
            Operario.ValidarPin(dto.Pin);
            var pinHash = _hasher.Hash(dto.Pin);
            if (pinHash != operario.PinHash)
            {
                await ValidarPinLibreAsync(pinHash);
            }
            operario.CambiarPin(pinHash);
            await _operarios.UpdateAsync(operario);
            return true;
        }

        private async Task ValidarPinLibreAsync(string pinHash)
        {
            if (await _operarios.GetActivoPorPinHashAsync(pinHash) is not null)
            {
                throw new ConflictoDeUnicidadException("Ese PIN ya lo usa otro operario. Elegí otro.");
            }
        }

        private static OperarioDto ADto(Operario operario) => new() { Id = operario.Id, Nombre = operario.Nombre, Activo = operario.Activo };
    }
}
