using DairyControl.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace DairyControl.Infrastructure.Persistence.Configurations
{
    internal class ProveedorConfiguration : IEntityTypeConfiguration<Proveedor>
    {
        public void Configure(EntityTypeBuilder<Proveedor> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Nombre)
                .IsRequired()
                .HasMaxLength(Proveedor.NombreMaxLength);
            // Inactive proveedores stay for good (their recepciones keep showing); the migration gives every existing row Activo = true
            builder.Property(p => p.Activo).IsRequired();
            // Concurrency token as a shadow property (the entity does not know about it): a stale rename is refused (409)
            builder.Property<byte[]>("RowVersion").IsRowVersion();
            // Who created it; rows from before users existed have none. A user is never deleted, so the FK restricts.
            builder.HasOne<Usuario>()
                .WithMany()
                .HasForeignKey(p => p.CreadoPorUsuarioId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
