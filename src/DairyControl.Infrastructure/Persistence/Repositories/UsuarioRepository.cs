using DairyControl.Domain.Entities;
using DairyControl.Domain.Exceptions;
using DairyControl.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DairyControl.Infrastructure.Persistence.Repositories
{
    public class UsuarioRepository : IUsuarioRepository
    {
        private readonly AppDbContext _context;

        public UsuarioRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<Usuario?> GetByIdAsync(Guid id) => await _context.Usuarios.FindAsync(id);

        public async Task<Usuario?> GetByUsernameAsync(string username) =>
            await _context.Usuarios.FirstOrDefaultAsync(u => u.Username == username);

        public async Task<bool> AnyAsync() => await _context.Usuarios.AsNoTracking().AnyAsync();

        public async Task<IReadOnlyList<Usuario>> GetAllAsync() => await _context.Usuarios.AsNoTracking().ToListAsync();

        // The unique index on Username is the last line of defense when two admins create the same user at once.
        public async Task AddAsync(Usuario usuario)
        {
            _context.Usuarios.Add(usuario);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (UnicidadSql.EsViolacion(ex))
            {
                _context.Entry(usuario).State = EntityState.Detached;
                throw new ConflictoDeUnicidadException("Ya existe un usuario con ese nombre de usuario.");
            }
        }

        public async Task UpdateAsync(Usuario usuario)
        {
            await _context.SaveChangesAsync();
        }

        private const int MaxReintentos = 10;

        public async Task<Usuario?> ModificarAsync(Guid id, Func<Usuario, bool> cambio)
        {
            var usuario = await _context.Usuarios.FindAsync(id);
            if (usuario is null)
            {
                return null;
            }
            for (var intento = 0; intento < MaxReintentos; intento++)
            {
                // Always start from what the database has now, not from what this request read earlier
                await _context.Entry(usuario).ReloadAsync();
                if (!cambio(usuario))
                {
                    return usuario;
                }
                try
                {
                    await _context.SaveChangesAsync();
                    return usuario;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Someone saved first: read again and apply the change on top of it
                }
            }
            throw new DemasiadosIntentosException("Demasiados intentos al mismo tiempo. Probá de nuevo en unos segundos.");
        }
    }
}
