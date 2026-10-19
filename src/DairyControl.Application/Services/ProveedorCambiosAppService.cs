using DairyControl.Application.DTOs;
using DairyControl.Application.Queries;
using DairyControl.Application.Seguridad;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Interfaces;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Application.Services
{
    /// <summary>
    /// Changes to an existing proveedor (Admin only: the controller names the policy): correcting the name, deactivating and reactivating.
    /// Each one needs a reason and writes a history entry; proveedores are never deleted and keep all their recepciones.
    /// </summary>
    public class ProveedorCambiosAppService
    {
        private readonly IProveedorRepository _proveedores;
        private readonly IProveedorQueries _queries;
        private readonly IHistorialQueries _historial;
        private readonly IUsuarioActual _actual;
        private readonly TimeProvider _reloj;

        public ProveedorCambiosAppService(IProveedorRepository proveedores, IProveedorQueries queries, IHistorialQueries historial, IUsuarioActual actual, TimeProvider reloj)
        {
            _proveedores = proveedores;
            _queries = queries;
            _historial = historial;
            _actual = actual;
            _reloj = reloj;
        }

        public async Task<ProveedorDto?> RenombrarAsync(Guid id, RenombrarProveedorDto dto)
        {
            var motivo = MotivoDeCambio.Create(dto.Motivo);
            var versionLeida = RecepcionAppService.LeerVersion(dto.Version);
            var usuarioId = UsuarioActualId();

            var leido = await _proveedores.GetLeidoAsync(id);
            if (leido is null)
                return null;

            var cambio = leido.Proveedor.Renombrar(dto.Nombre ?? string.Empty, motivo, usuarioId, _reloj.GetUtcNow().UtcDateTime);
            await _proveedores.UpdateAsync(leido.Proveedor, versionLeida, cambio);
            return await _queries.GetByIdAsync(id);
        }

        /// <summary>Returns false when the proveedor does not exist.</summary>
        public async Task<bool> DesactivarAsync(Guid id, MotivoCambioDto dto)
        {
            var motivo = MotivoDeCambio.Create(dto.Motivo);
            var usuarioId = UsuarioActualId();

            var leido = await _proveedores.GetLeidoAsync(id);
            if (leido is null)
                return false;

            var cambio = leido.Proveedor.Desactivar(motivo, usuarioId, _reloj.GetUtcNow().UtcDateTime);
            await _proveedores.UpdateAsync(leido.Proveedor, null, cambio);
            return true;
        }

        /// <summary>Returns false when the proveedor does not exist.</summary>
        public async Task<bool> ReactivarAsync(Guid id, MotivoCambioDto dto)
        {
            var motivo = MotivoDeCambio.Create(dto.Motivo);
            var usuarioId = UsuarioActualId();

            var leido = await _proveedores.GetLeidoAsync(id);
            if (leido is null)
                return false;

            var cambio = leido.Proveedor.Reactivar(motivo, usuarioId, _reloj.GetUtcNow().UtcDateTime);
            await _proveedores.UpdateAsync(leido.Proveedor, null, cambio);
            return true;
        }

        public Task<IReadOnlyList<CorreccionDto>?> GetHistorialAsync(Guid id) =>
            _historial.GetAsync(CambioRegistrado.EntidadProveedor, id);

        private Guid UsuarioActualId() =>
            _actual.Id ?? throw new InvalidOperationException("No hay un usuario autenticado en la solicitud.");
    }
}
