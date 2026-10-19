using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using DairyControl.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
namespace DairyControl.Infrastructure.Persistence.Repositories
{
    public class RecepcionRepository : IRecepcionRepository
    {
        // Shadow property (see RecepcionLecheConfiguration): the entity does not know about it
        private const string RowVersion = "RowVersion";

        private readonly AppDbContext _context;
        public RecepcionRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<TicketRegistrado?> GetTicketRegistradoAsync(int ticket)
        {
            return await _context.Recepciones
                .AsNoTracking()
                .Where(r => r.Ticket == ticket)
                .Join(_context.Proveedores, r => r.ProveedorId, p => p.Id, (r, p) => new TicketRegistrado(p.Nombre, r.FechaHora, r.AnuladaEn != null))
                .FirstOrDefaultAsync();
        }

        // The unique index on Ticket is the last line of defense when two operators save the same ticket at once.
        public async Task AddAsync(RecepcionLeche recepcion)
        {
            _context.Recepciones.Add(recepcion);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (UnicidadSql.EsViolacion(ex))
            {
                // The rejected recepción must not stay tracked as Added in the scoped context.
                _context.Entry(recepcion).State = EntityState.Detached;
                throw new TicketDuplicadoException("El ticket ya está registrado.");
            }
        }

        public async Task<RecepcionLeida?> GetByIdAsync(Guid id)
        {
            var recepcion = await _context.Recepciones.FirstOrDefaultAsync(r => r.Id == id);
            return recepcion is null ? null : new RecepcionLeida(recepcion, VersionActual(recepcion));
        }

        public async Task<byte[]> UpdateAsync(RecepcionLeche recepcion, byte[]? versionLeida, IReadOnlyList<CambioRegistrado> cambios)
        {
            if (versionLeida != null)
            {
                // The UPDATE only matches the row if it still has the version the caller read
                _context.Entry(recepcion).Property(RowVersion).OriginalValue = versionLeida;
            }
            // The history entries are saved in the same SaveChanges as the change they describe: both or none
            _context.HistorialCambios.AddRange(cambios);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                Descartar(cambios);
                throw new ConflictoDeConcurrenciaException();
            }
            catch (DbUpdateException ex) when (UnicidadSql.EsViolacion(ex))
            {
                Descartar(cambios);
                throw new TicketDuplicadoException("El ticket ya está registrado.");
            }
            return VersionActual(recepcion);
        }

        // The rejected entries must not stay tracked as Added in the scoped context.
        private void Descartar(IReadOnlyList<CambioRegistrado> cambios)
        {
            foreach (var cambio in cambios)
            {
                _context.Entry(cambio).State = EntityState.Detached;
            }
        }

        private byte[] VersionActual(RecepcionLeche recepcion) =>
            (byte[])_context.Entry(recepcion).Property(RowVersion).CurrentValue!;
    }
}
