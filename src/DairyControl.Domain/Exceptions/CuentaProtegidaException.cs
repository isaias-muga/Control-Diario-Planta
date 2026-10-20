namespace DairyControl.Domain.Exceptions;

/// <summary>Another Admin tried to deactivate, demote or reset the password of a protected (owner) account. The API answers 403.</summary>
public sealed class CuentaProtegidaException : ExcepcionDeDominio
{
    public CuentaProtegidaException(string mensaje) : base(mensaje) { }
}
