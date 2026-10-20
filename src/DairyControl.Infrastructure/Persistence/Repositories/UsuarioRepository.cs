using System.Data;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
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

        public async Task<IReadOnlyList<Usuario>> GetAllParaModificarAsync() => await _context.Usuarios.ToListAsync();

        // The unique index on Username is the last line of defense when two admins create the same user at once.
        public async Task AddAsync(Usuario usuario, CambioRegistrado cambio)
        {
            _context.Usuarios.Add(usuario);
            _context.HistorialCambios.Add(cambio);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (UnicidadSql.EsViolacion(ex))
            {
                _context.Entry(usuario).State = EntityState.Detached;
                _context.Entry(cambio).State = EntityState.Detached;
                throw new ConflictoDeUnicidadException("Ya existe un usuario con ese nombre de usuario.");
            }
        }

        public async Task UpdateAsync(Usuario usuario)
        {
            await _context.SaveChangesAsync();
        }

        public async Task GuardarCambioAsync(Usuario usuario, CambioRegistrado cambio, bool exigirOtroAdminActivo)
        {
            // The history entry is saved in the same SaveChanges as the change it describes: both or none
            _context.HistorialCambios.Add(cambio);
            try
            {
                if (!exigirOtroAdminActivo)
                {
                    await _context.SaveChangesAsync();
                    return;
                }
                // Serializable: the count and the save cannot be interleaved with another admin's change, so two admins cannot
                // deactivate or demote each other at the same time and leave nobody (an explicit transaction needs the execution strategy)
                await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var transaccion = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                    var otrosAdmins = await _context.Usuarios.AsNoTracking()
                        .CountAsync(u => u.Id != usuario.Id && u.Activo && u.Rol == RolUsuario.Admin);
                    if (otrosAdmins == 0)
                    {
                        throw new ReglaDeNegocioException(Usuario.MensajeUltimoAdmin, "id");
                    }
                    await _context.SaveChangesAsync();
                    await transaccion.CommitAsync();
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                // A login attempt (or another admin) saved this row after it was read
                _context.Entry(cambio).State = EntityState.Detached;
                throw new ConflictoDeConcurrenciaException("El usuario cambió mientras tanto. Recargá la página y probá de nuevo.");
            }
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
