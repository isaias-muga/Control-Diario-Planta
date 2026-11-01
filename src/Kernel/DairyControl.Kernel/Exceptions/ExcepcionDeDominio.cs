namespace DairyControl.Kernel.Exceptions;

/// <summary>
/// Base of every exception raised on purpose (a broken business rule, a duplicate, a stale edit).
/// The API maps each category to its HTTP status; anything that does not derive from it is a bug and a logged 500.
/// </summary>
public abstract class ExcepcionDeDominio : Exception
{
    protected ExcepcionDeDominio(string mensaje) : base(mensaje) { }
}
