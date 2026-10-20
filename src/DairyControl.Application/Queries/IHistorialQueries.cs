using DairyControl.Application.DTOs;

namespace DairyControl.Application.Queries
{
    // Read side of the history of changes (corrections, voids, deactivations): newest first, one item per correction.
    public interface IHistorialQueries
    {
        /// <summary>The history of a recepción or a proveedor (see CambioRegistrado.Entidad...), or null when it does not exist.</summary>
        Task<IReadOnlyList<CorreccionDto>?> GetAsync(string entidad, Guid id);
    }
}
