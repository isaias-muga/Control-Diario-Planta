namespace DairyControl.Kernel;

/// <summary>The validated user of the current request (implemented by the host from the token).</summary>
public interface IUsuarioActual
{
    Guid? Id { get; }
    RolUsuario? Rol { get; }
}
