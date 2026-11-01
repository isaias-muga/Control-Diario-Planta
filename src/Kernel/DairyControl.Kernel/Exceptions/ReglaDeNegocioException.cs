namespace DairyControl.Kernel.Exceptions;

/// <summary>A business rule or a plausible range was broken; <see cref="Campo"/> names the input that caused it, when there is one. The API answers 400.</summary>
public sealed class ReglaDeNegocioException(string mensaje, string? campo = null) : ExcepcionDeDominio(mensaje)
{
    public string? Campo { get; } = campo;
}
