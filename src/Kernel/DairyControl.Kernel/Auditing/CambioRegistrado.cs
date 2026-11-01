using DairyControl.Kernel.Exceptions;

namespace DairyControl.Kernel.Auditing;

/// <summary>
/// One field that changed (a correction, a void, a deactivation, a change to a user): what it was, what it is, who did it, when and why.
/// The history is append-only: nothing updates or deletes these rows. It refers to what it describes by id and accepts any entity name
/// of at most <see cref="EntidadMaxLength"/> characters; the names of entities and fields are constants of the module that owns them.
/// </summary>
public class CambioRegistrado
{
    public const int EntidadMaxLength = 20;
    public const int CampoMaxLength = 50;
    public const int ValorMaxLength = 500;

    public Guid Id { get; private set; }
    public string Entidad { get; private set; } = null!;
    public Guid EntidadId { get; private set; }

    /// <summary>Groups the entries of one correction (a correction of three fields writes three entries with the same id).</summary>
    public Guid CorreccionId { get; private set; }
    public string Campo { get; private set; } = null!;
    public string? ValorAnterior { get; private set; }
    public string? ValorNuevo { get; private set; }
    public Guid UsuarioId { get; private set; }

    /// <summary>UTC, from the TimeProvider.</summary>
    public DateTime EnUtc { get; private set; }
    public string Motivo { get; private set; } = null!;

    private CambioRegistrado() { }

    public static CambioRegistrado Registrar(string entidad, Guid entidadId, Guid correccionId, string campo, string? valorAnterior, string? valorNuevo,
        Guid usuarioId, DateTime enUtc, MotivoDeCambio motivo)
    {
        ArgumentNullException.ThrowIfNull(motivo);
        if (string.IsNullOrWhiteSpace(entidad) || entidad.Length > EntidadMaxLength)
        {
            throw new ReglaDeNegocioException("La entidad del cambio no es válida.", nameof(entidad));
        }
        if (entidadId == Guid.Empty || correccionId == Guid.Empty)
        {
            throw new ReglaDeNegocioException("El cambio debe indicar qué registro modifica.", nameof(entidadId));
        }
        if (usuarioId == Guid.Empty)
        {
            throw new ReglaDeNegocioException("El usuario que hace el cambio es obligatorio.", nameof(usuarioId));
        }
        if (string.IsNullOrWhiteSpace(campo) || campo.Length > CampoMaxLength)
        {
            throw new ReglaDeNegocioException("El campo del cambio no es válido.", nameof(campo));
        }
        if (enUtc == default)
        {
            throw new ReglaDeNegocioException("La fecha del cambio es obligatoria.", nameof(enUtc));
        }
        return new CambioRegistrado
        {
            Id = Guid.NewGuid(),
            Entidad = entidad,
            EntidadId = entidadId,
            CorreccionId = correccionId,
            Campo = campo,
            ValorAnterior = Recortar(valorAnterior),
            ValorNuevo = Recortar(valorNuevo),
            UsuarioId = usuarioId,
            EnUtc = enUtc,
            Motivo = motivo.Texto
        };
    }

    private static string? Recortar(string? valor) =>
        valor is { Length: > ValorMaxLength } ? valor[..ValorMaxLength] : valor;
}
