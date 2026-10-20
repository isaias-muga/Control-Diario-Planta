using DairyControl.Domain.Entities;

namespace DairyControl.Domain.Interfaces
{
    public interface IOperarioRepository
    {
        Task<Operario?> GetByIdAsync(Guid id);

        /// <summary>The active operario whose PIN hash is this one, if any.</summary>
        Task<Operario?> GetActivoPorPinHashAsync(string pinHash);
        Task<IReadOnlyList<Operario>> GetAllAsync();

        /// <summary>
        /// Saves a new operario together with its creation entry in the history (both or none).
        /// Throws <see cref="Exceptions.ConflictoDeUnicidadException"/> when another active operario has the same PIN.
        /// </summary>
        Task AddAsync(Operario operario, CambioRegistrado cambio);

        /// <summary>
        /// Saves the changes of an operario loaded from this repository together with the history entry that describes them (both or
        /// none). Throws <see cref="Exceptions.ConflictoDeUnicidadException"/> when another active operario has the same PIN.
        /// </summary>
        Task UpdateAsync(Operario operario, CambioRegistrado cambio);
    }
}
