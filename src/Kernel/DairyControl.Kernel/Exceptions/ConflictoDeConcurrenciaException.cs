namespace DairyControl.Kernel.Exceptions;

/// <summary>The row was changed by someone else after the caller read it.</summary>
public sealed class ConflictoDeConcurrenciaException : ConflictoException
{
    public const string MensajePredeterminado = "Los límites fueron modificados por otro usuario. Recargá la página.";

    public ConflictoDeConcurrenciaException() : base(MensajePredeterminado) { }

    public ConflictoDeConcurrenciaException(string mensaje) : base(mensaje) { }
}
