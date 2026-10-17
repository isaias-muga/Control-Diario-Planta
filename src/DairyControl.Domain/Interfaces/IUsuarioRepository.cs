using DairyControl.Domain.Entities;

namespace DairyControl.Domain.Interfaces
{
    public interface IUsuarioRepository
    {
        Task<Usuario?> GetByIdAsync(Guid id);
        Task<Usuario?> GetByUsernameAsync(string username);
        Task<bool> AnyAsync();
        Task<IReadOnlyList<Usuario>> GetAllAsync();

        /// <summary>Throws <see cref="Exceptions.ConflictoDeUnicidadException"/> when the username is taken.</summary>
        Task AddAsync(Usuario usuario);

        /// <summary>Saves the changes of a user loaded from this repository.</summary>
        Task UpdateAsync(Usuario usuario);

        /// <summary>
        /// Applies a change to the CURRENT state of the user and saves it, retrying when someone else saved in between (the row has a
        /// version). The attempt counters rely on it: attempts made at the same time must each be counted. The change returns false
        /// when there is nothing to save. Returns the user, or null if it does not exist.
        /// </summary>
        Task<Usuario?> ModificarAsync(Guid id, Func<Usuario, bool> cambio);
    }
}
