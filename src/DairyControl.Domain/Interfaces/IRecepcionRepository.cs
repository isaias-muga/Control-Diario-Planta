using DairyControl.Domain.Entities;
using DairyControl.Domain.ValueObjects;
namespace DairyControl.Domain.Interfaces
{
    public interface IRecepcionRepository
    {
        Task AddAsync(RecepcionLeche recepcion);

        Task<TicketRegistrado?> GetTicketRegistradoAsync(int ticket);

        /// <summary>The recepción (tracked, so a change can be saved) with the version it had when it was read.</summary>
        Task<RecepcionLeida?> GetByIdAsync(Guid id);

        /// <summary>
        /// Saves the changes of the recepción and the history entries that describe them in one go. If <paramref name="versionLeida"/> is
        /// given and the stored version is different, it throws <see cref="Exceptions.ConflictoDeConcurrenciaException"/>; a ticket that is
        /// taken throws <see cref="Exceptions.TicketDuplicadoException"/>. Returns the new version.
        /// </summary>
        Task<byte[]> UpdateAsync(RecepcionLeche recepcion, byte[]? versionLeida, IReadOnlyList<CambioRegistrado> cambios);
    }

    /// <summary>A recepción and its concurrency version, which is kept out of the entity.</summary>
    public sealed class RecepcionLeida
    {
        public RecepcionLeida(RecepcionLeche recepcion, byte[] version)
        {
            Recepcion = recepcion;
            Version = version;
        }

        public RecepcionLeche Recepcion { get; }
        public byte[] Version { get; }
    }
}
