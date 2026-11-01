namespace DairyControl.Kernel.Exceptions;

/// <summary>Category: the request conflicts with the current state (a duplicate, a stale edit). The API answers 409.</summary>
public abstract class ConflictoException(string mensaje) : ExcepcionDeDominio(mensaje);
