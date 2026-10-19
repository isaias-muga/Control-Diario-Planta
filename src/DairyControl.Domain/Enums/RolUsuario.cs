namespace DairyControl.Domain.Enums;

/// <summary>What a person may do: Admin everything, Oficina reads and registers recepciones, Consulta only reads, Recepcion only loads recepciones.</summary>
public enum RolUsuario
{
    Admin,
    Oficina,
    Recepcion,
    Consulta
}
