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
            // The usernames are compared case-insensitively by the database collation (SQL Server default)
            builder.HasIndex(u => u.Username).IsUnique();
        }
    }
}
