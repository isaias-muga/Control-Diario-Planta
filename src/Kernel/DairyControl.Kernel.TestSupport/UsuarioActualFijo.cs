namespace DairyControl.Kernel.TestSupport;

/// <summary>An <see cref="IUsuarioActual"/> that answers the two values it was built with.</summary>
public sealed class UsuarioActualFijo(Guid? id, RolUsuario? rol) : IUsuarioActual
{
    public Guid? Id => id;
    public RolUsuario? Rol => rol;
}
