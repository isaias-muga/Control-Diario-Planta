using DairyControl.Application.DTOs;
using DairyControl.Application.Queries;
using DairyControl.Application.Seguridad;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using DairyControl.Domain.ValueObjects;
using System.Globalization;

namespace DairyControl.Application.Services
{
    public class ProveedorAppService
    {
        private readonly IProveedorRepository _proveedores;
        private readonly IRecepcionRepository _recepciones;
        private readonly IProveedorQueries _queries;
        private readonly IConfiguracionCalidadRepository _configuracion;
        private readonly IUsuarioActual _actual;
        private readonly VerificacionPinService _pin;
        private readonly TimeProvider _reloj;

        public ProveedorAppService(IProveedorRepository proveedores, IRecepcionRepository recepciones, IProveedorQueries queries,
            IConfiguracionCalidadRepository configuracion, IUsuarioActual actual, VerificacionPinService pin, TimeProvider reloj)
        {
            _configuracion = configuracion;
            _actual = actual;
            _pin = pin;
            _proveedores = proveedores;
            _recepciones = recepciones;
            _queries = queries;
            _reloj = reloj;
        }

        public async Task<ProveedorDto> CrearAsync(CrearProveedorDto dto)
        {
            var proveedor = Proveedor.Crear(dto.Nombre, UsuarioActualId());
            await _proveedores.AddAsync(proveedor);

            return new ProveedorDto
            {
                Id = proveedor.Id,
                Nombre = proveedor.Nombre,
                CantidadRecepciones = 0
            };
        }

        public Task<ProveedorDto?> GetByIdAsync(Guid id) => _queries.GetByIdAsync(id);

        public Task<IReadOnlyList<ProveedorDto>> GetAllAsync() => _queries.GetAllAsync();

        /// <summary>Id and name only, for choosing a proveedor for a new recepción (the reception phone gets no counts). Inactive ones are left out.</summary>
        public async Task<IReadOnlyList<ProveedorOpcionDto>> GetOpcionesAsync()
        {
            var proveedores = await _proveedores.GetAllAsync();
            return proveedores.Where(p => p.Activo).Select(p => new ProveedorOpcionDto { Id = p.Id, Nombre = p.Nombre }).ToList();
        }

        public async Task<RecepcionRegistradaDto?> RegistrarRecepcionAsync(Guid proveedorId, RegistrarRecepcionDto dto)
        {
            var proveedor = await _proveedores.GetByIdAsync(proveedorId);
            if (proveedor is null)
                return null;
            if (!proveedor.Activo)
                throw new ReglaDeNegocioException("El proveedor está desactivado: no se le pueden registrar recepciones.", "proveedorId");

            // The audit data comes from the token and the PIN, never from the request body
            var usuarioId = UsuarioActualId();
            var operario = await _pin.ResolverAsync(dto.Pin);

            // The silo must exist in this plant: its number of silos is data, read now (history is never re-checked)
            var configuracion = await _configuracion.GetAsync()
                ?? throw new InvalidOperationException("Falta la configuración de la planta.");
            configuracion.Configuracion.ValidarSilo(dto.Silo);

            var parametros = ParametrosCalidad.Create(dto.Grasa, dto.Acidez, dto.Temperatura, dto.Litros, dto.AguaPorcentaje);

            // The ticket is unique across the whole plant. A non-positive ticket is left to the domain (400).
            var ticket = dto.Ticket is > 0 ? dto.Ticket : null;
            if (ticket is int numeroTicket)
            {
                var existente = await _recepciones.GetTicketRegistradoAsync(numeroTicket);
                if (existente is not null)
                    throw new TicketDuplicadoException(MensajeTicketDuplicado(numeroTicket, existente));
            }

            // A missing date becomes default(DateTime), which the domain rejects.
            var recepcion = RecepcionLeche.Registrar(proveedorId, parametros, HoraPlanta.ALaPlanta(dto.FechaHora.GetValueOrDefault()), HoraPlanta.Ahora(_reloj), dto.Silo, dto.Observaciones,
                usuarioId, operario?.Id, _reloj.GetUtcNow().UtcDateTime, dto.Ticket);

            try
            {
                await _recepciones.AddAsync(recepcion);
            }
            catch (TicketDuplicadoException) when (ticket is not null)
            {
                // Lost a race with another operator: same 409, now with the details of the winner.
                var existente = await _recepciones.GetTicketRegistradoAsync(ticket.Value);
                throw new TicketDuplicadoException(MensajeTicketDuplicado(ticket.Value, existente));
            }

            return new RecepcionRegistradaDto
            {
                Id = recepcion.Id,
                Ticket = recepcion.Ticket,
                Litros = recepcion.Parametros.Litros,
                ProveedorNombre = proveedor.Nombre,
                OperarioNombre = operario?.Nombre
            };
        }

        private Guid UsuarioActualId() =>
            _actual.Id ?? throw new InvalidOperationException("No hay un usuario autenticado en la solicitud.");

        // A voided recepción keeps its ticket used: the message says so, because the number looks free in the list
        internal static string MensajeTicketDuplicado(int ticket, TicketRegistrado? existente) =>
            existente is null
                ? $"El ticket {ticket} ya está registrado"
                : string.Create(CultureInfo.InvariantCulture,
                    $"El ticket {ticket} ya está registrado{(existente.Anulada ? " en una recepción anulada, y no se puede reutilizar" : string.Empty)} ({existente.ProveedorNombre}, {existente.FechaHora:dd/MM/yyyy})");
    }
}
