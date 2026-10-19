using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
namespace DairyControl.Infrastructure.Persistence.Repositories
{
    public class ProveedorRepository : IProveedorRepository
    {
        // Shadow property (see ProveedorConfiguration): the entity does not know about it
        private const string RowVersion = "RowVersion";

        private readonly AppDbContext _context;
        public ProveedorRepository(AppDbContext context)
        {
            _context = context;
        }
        public async Task<Proveedor?> GetByIdAsync(Guid id)
        {
            return await _context.Proveedores.FindAsync(id);
        }
        public async Task<IReadOnlyList<Proveedor>> GetAllAsync()
        {
            return await _context.Proveedores.AsNoTracking().ToListAsync();
        }

        public async Task<bool> ExistsAsync(Guid id)
        {
            return await _context.Proveedores.AsNoTracking().AnyAsync(p => p.Id == id);
        }

        public async Task AddAsync(Proveedor proveedor)
        {
            _context.Proveedores.Add(proveedor);
            await _context.SaveChangesAsync();
        }

        public async Task<ProveedorLeido?> GetLeidoAsync(Guid id)
        {
            var proveedor = await _context.Proveedores.FirstOrDefaultAsync(p => p.Id == id);
            return proveedor is null ? null : new ProveedorLeido(proveedor, VersionActual(proveedor));
        }

        public async Task<byte[]> UpdateAsync(Proveedor proveedor, byte[]? versionLeida, CambioRegistrado cambio)
        {
            if (versionLeida != null)
            {
                // The UPDATE only matches the row if it still has the version the caller read
                _context.Entry(proveedor).Property(RowVersion).OriginalValue = versionLeida;
            }
            // The history entry is saved in the same SaveChanges as the change it describes: both or none
            _context.HistorialCambios.Add(cambio);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.Entry(cambio).State = EntityState.Detached;
                throw new ConflictoDeConcurrenciaException();
            }
            return VersionActual(proveedor);
        }

        private byte[] VersionActual(Proveedor proveedor) =>
            (byte[])_context.Entry(proveedor).Property(RowVersion).CurrentValue!;
    }
}
