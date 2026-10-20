using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DairyControl.Infrastructure.Persistence.Repositories
{
    public class OperarioRepository : IOperarioRepository
    {
        private readonly AppDbContext _context;

        public OperarioRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<Operario?> GetByIdAsync(Guid id) => await _context.Operarios.FindAsync(id);

        public async Task<Operario?> GetActivoPorPinHashAsync(string pinHash) =>
            await _context.Operarios.AsNoTracking().FirstOrDefaultAsync(o => o.Activo && o.PinHash == pinHash);

        public async Task<IReadOnlyList<Operario>> GetAllAsync() => await _context.Operarios.AsNoTracking().ToListAsync();

        // The filtered unique index on PinHash is the last line of defense when two admins pick the same PIN at once.
        public async Task AddAsync(Operario operario, CambioRegistrado cambio)
        {
            _context.Operarios.Add(operario);
            _context.HistorialCambios.Add(cambio);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (UnicidadSql.EsViolacion(ex))
            {
                _context.Entry(operario).State = EntityState.Detached;
                _context.Entry(cambio).State = EntityState.Detached;
                throw new ConflictoDeUnicidadException("Ese PIN ya lo usa otro operario. Elegí otro.");
            }
        }

        // The history entry is saved in the same SaveChanges as the change it describes: both or none
        public async Task UpdateAsync(Operario operario, CambioRegistrado cambio)
        {
            _context.HistorialCambios.Add(cambio);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (UnicidadSql.EsViolacion(ex))
            {
                _context.Entry(cambio).State = EntityState.Detached;
                throw new ConflictoDeUnicidadException("Ese PIN ya lo usa otro operario. Elegí otro.");
            }
        }
    }
}
