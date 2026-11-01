namespace DairyControl.Kernel.Exceptions;

/// <summary>Too many failed attempts (login or PIN); the check is refused for a while. The API answers 429.</summary>
public sealed class DemasiadosIntentosException : ExcepcionDeDominio
{
    public DemasiadosIntentosException(string mensaje) : base(mensaje) { }
}
