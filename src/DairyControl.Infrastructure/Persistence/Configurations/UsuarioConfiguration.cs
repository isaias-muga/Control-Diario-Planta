using DairyControl.Domain.Entities;
using DairyControl.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DairyControl.Infrastructure.Persistence.Configurations
{
    internal class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
    {
        public void Configure(EntityTypeBuilder<Usuario> builder)
        {
            builder.ToTable("Usuarios");
            builder.HasKey(u => u.Id);
            builder.Property(u => u.Id).ValueGeneratedNever();
            builder.Property(u => u.Username).IsRequired().HasMaxLength(Usuario.UsernameMaxLength);
            builder.Property(u => u.Nombre).IsRequired().HasMaxLength(Usuario.NombreMaxLength);
            builder.Property(u => u.Rol).IsRequired().HasConversion<string>().HasMaxLength(20);
            builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(500);
            // Set only from configuration at startup (AdminUser:Protegidos), never through the API
            builder.Property(u => u.Protegido);
            // The attempt counters are changed by concurrent requests: a stale write must not overwrite a newer one
            builder.Property<byte[]>("RowVersion").IsRowVersion();
            // The usernames are compared case-insensitively by the database collation (SQL Server default)
            builder.HasIndex(u => u.Username).IsUnique();
        }
    }
}
