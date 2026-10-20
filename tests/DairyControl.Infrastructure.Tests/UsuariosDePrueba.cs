using DairyControl.Application.Seguridad;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using DairyControl.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DairyControl.Infrastructure.Tests
{
    /// <summary>The foreign keys of the audit columns need a real user row: tests create an admin once and use its id.</summary>
    internal static class UsuariosDePrueba
    {
        /// <summary>
        /// SQL Server fills the rowversion of Usuarios by itself; SQLite does not, so tests install triggers that give every inserted and
        /// every updated row a new value. They prove the repository logic, not the SQL Server column.
        /// </summary>
        public static void InstalarVersionDeFila(AppDbContext context)
        {
            context.Database.ExecuteSqlRaw(
                "CREATE TRIGGER Usuarios_RowVersion_Insert AFTER INSERT ON Usuarios BEGIN UPDATE Usuarios SET RowVersion = randomblob(8) WHERE Id = NEW.Id; END;");
            context.Database.ExecuteSqlRaw(
                "CREATE TRIGGER Usuarios_RowVersion_Update AFTER UPDATE OF Username, Nombre, Rol, Activo, PasswordHash, SecurityStamp, DebeCambiarPassword, " +
                "FallosLogin, LoginBloqueadoHastaUtc, FallosPin, PrimerFalloPinUtc, PinBloqueadoHastaUtc, Protegido ON Usuarios " +
                "BEGIN UPDATE Usuarios SET RowVersion = randomblob(8) WHERE Id = NEW.Id; END;");
        }

        /// <summary>
        /// The same for Recepciones and Proveedores, which also have a rowversion now (a stale correction is refused). Called by the tests that
        /// need to see a concurrency conflict; a trigger fires on any update of the row.
        /// </summary>
        public static void InstalarVersionDeFilaDeRecepcionesYProveedores(AppDbContext context)
        {
            context.Database.ExecuteSqlRaw("CREATE TRIGGER Recepciones_RowVersion_Insert AFTER INSERT ON Recepciones BEGIN UPDATE Recepciones SET RowVersion = randomblob(8) WHERE Id = NEW.Id; END;");
            context.Database.ExecuteSqlRaw("CREATE TRIGGER Recepciones_RowVersion_Update AFTER UPDATE ON Recepciones BEGIN UPDATE Recepciones SET RowVersion = randomblob(8) WHERE Id = NEW.Id; END;");
            context.Database.ExecuteSqlRaw("CREATE TRIGGER Proveedores_RowVersion_Insert AFTER INSERT ON Proveedores BEGIN UPDATE Proveedores SET RowVersion = randomblob(8) WHERE Id = NEW.Id; END;");
            context.Database.ExecuteSqlRaw("CREATE TRIGGER Proveedores_RowVersion_Update AFTER UPDATE ON Proveedores BEGIN UPDATE Proveedores SET RowVersion = randomblob(8) WHERE Id = NEW.Id; END;");
        }

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
