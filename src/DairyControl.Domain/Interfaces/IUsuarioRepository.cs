using DairyControl.Domain.Entities;

namespace DairyControl.Domain.Interfaces
{
    public interface IUsuarioRepository
    {
        Task<Usuario?> GetByIdAsync(Guid id);
        Task<Usuario?> GetByUsernameAsync(string username);
        Task<bool> AnyAsync();
        Task<IReadOnlyList<Usuario>> GetAllAsync();

        /// <summary>
        /// Saves a new user together with its creation entry in the history (both or none).
        /// Throws <see cref="Exceptions.ConflictoDeUnicidadException"/> when the username is taken.
        /// </summary>
        Task AddAsync(Usuario usuario, CambioRegistrado cambio);

        /// <summary>Saves the changes of a user loaded from this repository.</summary>
        Task UpdateAsync(Usuario usuario);

        /// <summary>
        /// Saves a change to a user loaded from this repository together with its history entry (both or none). When
        /// <paramref name="exigirOtroAdminActivo"/> is true the change would remove an active Admin: inside a serializable transaction it
        /// checks that another active Admin exists and throws <see cref="Exceptions.ReglaDeNegocioException"/> if not, so two Admins who
        /// demote each other at the same time cannot both succeed. Throws <see cref="Exceptions.ConflictoDeConcurrenciaException"/> when
        /// the row was changed by someone else in between.
        /// </summary>
        Task GuardarCambioAsync(Usuario usuario, CambioRegistrado cambio, bool exigirOtroAdminActivo);

        /// <summary>
        /// Applies a change to the CURRENT state of the user and saves it, retrying when someone else saved in between (the row has a
        /// version). The attempt counters rely on it: attempts made at the same time must each be counted. The change returns false
        /// when there is nothing to save. Returns the user, or null if it does not exist.
        /// </summary>
        Task<Usuario?> ModificarAsync(Guid id, Func<Usuario, bool> cambio);
    }
}
