using DairyControl.Application.DTOs;
using DairyControl.Application.Queries;
using DairyControl.Application.Seguridad;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Application.Services
{
    /// <summary>
    /// Corrections and voids of recepciones (Admin only: the controller names the policy). Nothing is overwritten without a history entry
    /// and nothing is deleted: a correction records old and new value with a reason, a void keeps the row and its ticket.
    /// </summary>
    public class RecepcionAppService
    {
        private readonly IRecepcionRepository _recepciones;
        private readonly IProveedorRepository _proveedores;
        private readonly IConfiguracionCalidadRepository _configuracion;
        private readonly IHistorialQueries _historial;
        private readonly IUsuarioActual _actual;
        private readonly TimeProvider _reloj;

        public RecepcionAppService(IRecepcionRepository recepciones, IProveedorRepository proveedores, IConfiguracionCalidadRepository configuracion,
            IHistorialQueries historial, IUsuarioActual actual, TimeProvider reloj)
        {
            _recepciones = recepciones;
            _proveedores = proveedores;
            _configuracion = configuracion;
            _historial = historial;
            _actual = actual;
            _reloj = reloj;
        }

        public async Task<RecepcionDetalleDto?> GetDetalleAsync(Guid id)
        {
            var leida = await _recepciones.GetByIdAsync(id);
            return leida is null ? null : await ADetalleAsync(leida.Recepcion, leida.Version);
        }

        public async Task<RecepcionDetalleDto?> CorregirAsync(Guid id, CorregirRecepcionDto dto)
        {
            var motivo = MotivoDeCambio.Create(dto.Motivo);
            var versionLeida = LeerVersion(dto.Version);
            var usuarioId = UsuarioActualId();

            var leida = await _recepciones.GetByIdAsync(id);
            if (leida is null)
                return null;
            var recepcion = leida.Recepcion;

            if (dto.ProveedorId is not Guid proveedorId || proveedorId == Guid.Empty)
                throw new ReglaDeNegocioException("El proveedor es obligatorio.", "proveedorId");
            var nuevoProveedor = await _proveedores.GetByIdAsync(proveedorId)
                ?? throw new ReglaDeNegocioException("El proveedor no existe.", "proveedorId");
            // An inactive proveedor cannot receive a recepción; one that is already the recepción's keeps it (history is not re-checked)
            if (proveedorId != recepcion.ProveedorId && !nuevoProveedor.Activo)
                throw new ReglaDeNegocioException("El proveedor está desactivado: no se le pueden asignar recepciones.", "proveedorId");
            var anterior = proveedorId == recepcion.ProveedorId
                ? nuevoProveedor
                : await _proveedores.GetByIdAsync(recepcion.ProveedorId);

            // The silo must exist in this plant, but a silo that did not change is history: it is not re-checked
            if (dto.Silo != recepcion.Silo)
            {
                var configuracion = await _configuracion.GetAsync()
                    ?? throw new InvalidOperationException("Falta la configuración de la planta.");
                configuracion.Configuracion.ValidarSilo(dto.Silo);
            }

            var parametros = ParametrosCalidad.Create(dto.Grasa, dto.Acidez, dto.Temperatura, dto.Litros, dto.AguaPorcentaje);

            // The ticket is unique across the whole plant (voided recepciones keep theirs). A non-positive ticket is left to the domain (400).
            var ticket = dto.Ticket is > 0 && dto.Ticket != recepcion.Ticket ? dto.Ticket : null;
            if (ticket is int numeroTicket)
            {
                var existente = await _recepciones.GetTicketRegistradoAsync(numeroTicket);
                if (existente is not null)
                    throw new TicketDuplicadoException(ProveedorAppService.MensajeTicketDuplicado(numeroTicket, existente));
            }

            var cambios = recepcion.Corregir(proveedorId, anterior?.Nombre ?? string.Empty, nuevoProveedor.Nombre, parametros,
                HoraPlanta.ALaPlanta(dto.FechaHora.GetValueOrDefault()), HoraPlanta.Ahora(_reloj), dto.Silo, dto.Observaciones, dto.Ticket,
                motivo, usuarioId, _reloj.GetUtcNow().UtcDateTime);

            byte[] nuevaVersion;
            try
            {
                nuevaVersion = await _recepciones.UpdateAsync(recepcion, versionLeida, cambios);
            }
            catch (TicketDuplicadoException) when (ticket is not null)
            {
                // Lost a race with another operator: same 409, now with the details of the winner.
                var existente = await _recepciones.GetTicketRegistradoAsync(ticket.Value);
                throw new TicketDuplicadoException(ProveedorAppService.MensajeTicketDuplicado(ticket.Value, existente));
            }
            return await ADetalleAsync(recepcion, nuevaVersion);
        }

        /// <summary>Voids the recepción for good (it stays, with its ticket used). Returns false when it does not exist.</summary>
        public async Task<bool> AnularAsync(Guid id, MotivoCambioDto dto)
        {
            var motivo = MotivoDeCambio.Create(dto.Motivo);
            var usuarioId = UsuarioActualId();

            var leida = await _recepciones.GetByIdAsync(id);
            if (leida is null)
                return false;

            var cambio = leida.Recepcion.Anular(motivo, usuarioId, _reloj.GetUtcNow().UtcDateTime);
            // No version from the client: the row read a moment ago is the one voided, and a concurrent change still gives a 409
            await _recepciones.UpdateAsync(leida.Recepcion, null, [cambio]);
            return true;
        }

        public Task<IReadOnlyList<CorreccionDto>?> GetHistorialAsync(Guid id) =>
            _historial.GetAsync(CambioRegistrado.EntidadRecepcion, id);

        private async Task<RecepcionDetalleDto> ADetalleAsync(RecepcionLeche recepcion, byte[] version)
        {
            var proveedor = await _proveedores.GetByIdAsync(recepcion.ProveedorId);
            return new RecepcionDetalleDto
            {
                Id = recepcion.Id,
                ProveedorId = recepcion.ProveedorId,
                ProveedorNombre = proveedor?.Nombre ?? string.Empty,
                ProveedorActivo = proveedor?.Activo ?? true,
                Ticket = recepcion.Ticket,
                FechaHora = recepcion.FechaHora,
                Litros = recepcion.Parametros.Litros,
                AguaPorcentaje = recepcion.Parametros.AguaPorcentaje,
                Grasa = recepcion.Parametros.Grasa,
                Acidez = recepcion.Parametros.Acidez,
                Temperatura = recepcion.Parametros.Temperatura,
                Silo = recepcion.Silo,
                Observaciones = recepcion.Observaciones,
                Anulada = recepcion.EstaAnulada,
                Version = Convert.ToBase64String(version)
            };
        }

        // The version tells the repository which state the user was looking at; without it a stale edit would go unnoticed.
        internal static byte[] LeerVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                throw new ReglaDeNegocioException("Falta la versión del registro. Recargá la página.", "version");

            var bytes = new byte[version.Length];
            if (!Convert.TryFromBase64String(version, bytes, out var escritos) || escritos == 0)
                throw new ReglaDeNegocioException("La versión del registro no es válida. Recargá la página.", "version");

            return bytes[..escritos];
        }

        private Guid UsuarioActualId() =>
            _actual.Id ?? throw new InvalidOperationException("No hay un usuario autenticado en la solicitud.");
    }
}
