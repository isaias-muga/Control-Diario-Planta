using DairyControl.Domain.Entities;

namespace DairyControl.Domain.Interfaces
{
    public interface IOperarioRepository
    {
        Task<Operario?> GetByIdAsync(Guid id);

        /// <summary>The active operario whose PIN hash is this one, if any.</summary>
        Task<Operario?> GetActivoPorPinHashAsync(string pinHash);
        Task<IReadOnlyList<Operario>> GetAllAsync();

        /// <summary>Throws <see cref="Exceptions.ConflictoDeUnicidadException"/> when another active operario has the same PIN.</summary>
        Task AddAsync(Operario operario);

        /// <summary>Saves the changes of an operario loaded from this repository.</summary>
        Task UpdateAsync(Operario operario);
    }
}
