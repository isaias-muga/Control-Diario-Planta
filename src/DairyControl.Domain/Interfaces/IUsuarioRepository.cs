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
    }
}
