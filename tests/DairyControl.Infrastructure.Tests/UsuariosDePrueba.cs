using DairyControl.Application.Seguridad;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Infrastructure.Persistence;

namespace DairyControl.Infrastructure.Tests
{
    /// <summary>The foreign keys of the audit columns need a real user row: tests create an admin once and use its id.</summary>
    internal static class UsuariosDePrueba
    {
        /// <summary>Saves an Admin (hash "hash") and returns its id.</summary>
        public static Guid CrearAdmin(AppDbContext context, string username = "admin")
        {
            var admin = Usuario.Crear(username, "Administrador", RolUsuario.Admin, "hash", debeCambiarPassword: false);
            context.Usuarios.Add(admin);
            context.SaveChanges();
            return admin.Id;
        }
    }

    internal sealed class UsuarioActualFijo(Guid id, RolUsuario rol) : IUsuarioActual
    {
        public Guid? Id => id;

        public RolUsuario? Rol => rol;
    }
}
