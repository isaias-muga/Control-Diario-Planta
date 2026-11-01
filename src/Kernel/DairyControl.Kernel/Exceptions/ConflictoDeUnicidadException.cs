namespace DairyControl.Kernel.Exceptions;

/// <summary>A value that must be unique (a username, an operario PIN) is already taken. The API answers 409.</summary>
public sealed class ConflictoDeUnicidadException : ConflictoException
{
    public ConflictoDeUnicidadException(string mensaje) : base(mensaje) { }
}
