using DairyControl.Application.DTOs;
using DairyControl.Application.Queries;
using DairyControl.Application.Seguridad;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Application.Services
{
    public class OperarioAppService
    {
        private readonly IOperarioRepository _operarios;
        private readonly IHasherDePin _hasher;
        private readonly IUsuarioActual _actual;
        private readonly IHistorialQueries _historial;
        private readonly TimeProvider _reloj;

        public OperarioAppService(IOperarioRepository operarios, IHasherDePin hasher, IUsuarioActual actual, IHistorialQueries historial, TimeProvider reloj)
        {
            _operarios = operarios;
            _hasher = hasher;
            _actual = actual;
            _historial = historial;
            _reloj = reloj;
        }

        public async Task<IReadOnlyList<OperarioDto>> GetAllAsync()
        {
            var operarios = await _operarios.GetAllAsync();
            return operarios.OrderBy(o => o.Nombre, StringComparer.CurrentCultureIgnoreCase).Select(ADto).ToList();
        }

        public async Task<OperarioDto> CrearAsync(CrearOperarioDto dto)
        {
            Operario.ValidarPin(dto.Pin);
            var actorId = ActorId();
            var pinHash = _hasher.Hash(dto.Pin);
            await ValidarPinLibreAsync(pinHash);

            var operario = Operario.Crear(dto.Nombre, pinHash);
            await _operarios.AddAsync(operario, operario.RegistrarAlta(actorId, Ahora()));
            return ADto(operario);
        }

        /// <summary>Returns false when the operario does not exist.</summary>
        public async Task<bool> DesactivarAsync(Guid id, MotivoCambioDto dto)
        {
            var motivo = MotivoDeCambio.Create(dto.Motivo);
            var actorId = ActorId();

            var operario = await _operarios.GetByIdAsync(id);
            if (operario is null)
            {
                return false;
            }
            var cambio = operario.Desactivar(motivo, actorId, Ahora());
            await _operarios.UpdateAsync(operario, cambio);
            return true;
        }

        /// <summary>
        /// Its PIN works again, so no other active operario may have the same PIN: 409 otherwise (the admin deactivates the other
        /// operario first). Returns false when the operario does not exist.
        /// </summary>
        public async Task<bool> ReactivarAsync(Guid id, MotivoCambioDto dto)
        {
            var motivo = MotivoDeCambio.Create(dto.Motivo);
            var actorId = ActorId();

            var operario = await _operarios.GetByIdAsync(id);
            if (operario is null)
            {
                return false;
            }
            if (!operario.Activo)
            {
                await ValidarPinLibreAsync(operario.PinHash);
            }
            var cambio = operario.Reactivar(motivo, actorId, Ahora());
            await _operarios.UpdateAsync(operario, cambio);
            return true;
        }

        /// <summary>Returns false when the operario does not exist.</summary>
        public async Task<bool> CambiarPinAsync(Guid id, CambiarPinDto dto)
        {
            var actorId = ActorId();
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
            var cambio = operario.CambiarPin(pinHash, actorId, Ahora());
            await _operarios.UpdateAsync(operario, cambio);
            return true;
        }

        /// <summary>Null when the operario does not exist.</summary>
        public Task<IReadOnlyList<CorreccionDto>?> GetHistorialAsync(Guid id) =>
            _historial.GetAsync(CambioRegistrado.EntidadOperario, id);

        private async Task ValidarPinLibreAsync(string pinHash)
        {
            if (await _operarios.GetActivoPorPinHashAsync(pinHash) is not null)
            {
                throw new ConflictoDeUnicidadException("Ese PIN ya lo usa otro operario. Elegí otro.");
            }
        }

        private DateTime Ahora() => _reloj.GetUtcNow().UtcDateTime;

        private Guid ActorId() =>
            _actual.Id ?? throw new InvalidOperationException("No hay un usuario autenticado en la solicitud.");

        private static OperarioDto ADto(Operario operario) => new() { Id = operario.Id, Nombre = operario.Nombre, Activo = operario.Activo };
    }
}
