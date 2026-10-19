using DairyControl.Domain.Entities;
namespace DairyControl.Domain.Interfaces
{
    public interface IProveedorRepository
    {
        Task<Proveedor?> GetByIdAsync(Guid id);

        Task<IReadOnlyList<Proveedor>> GetAllAsync();

        Task<bool> ExistsAsync(Guid id);

        Task AddAsync(Proveedor proveedor);

        /// <summary>The proveedor (tracked, so a change can be saved) with the version it had when it was read.</summary>
        Task<ProveedorLeido?> GetLeidoAsync(Guid id);

        /// <summary>
        /// Saves the changes of the proveedor and the history entry that describes them in one go. If <paramref name="versionLeida"/> is
        /// given and the stored version is different, it throws <see cref="Exceptions.ConflictoDeConcurrenciaException"/>. Returns the new version.
        /// </summary>
        Task<byte[]> UpdateAsync(Proveedor proveedor, byte[]? versionLeida, CambioRegistrado cambio);
    }

    /// <summary>A proveedor and its concurrency version, which is kept out of the entity.</summary>
    public sealed class ProveedorLeido
    {
        public ProveedorLeido(Proveedor proveedor, byte[] version)
        {
            Proveedor = proveedor;
            Version = version;
        }

        public Proveedor Proveedor { get; }
        public byte[] Version { get; }
    }
}
