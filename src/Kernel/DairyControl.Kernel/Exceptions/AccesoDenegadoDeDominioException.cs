namespace DairyControl.Kernel.Exceptions;

/// <summary>Category: a domain rule forbids the action for this caller. The API answers 403.</summary>
public abstract class AccesoDenegadoDeDominioException(string mensaje) : ExcepcionDeDominio(mensaje);
