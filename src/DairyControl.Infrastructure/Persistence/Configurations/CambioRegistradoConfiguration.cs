using DairyControl.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace DairyControl.Infrastructure.Persistence.Configurations
{
    // The history of corrections, voids and deactivations. Append-only: nothing in the system updates or deletes these rows.
    internal class CambioRegistradoConfiguration : IEntityTypeConfiguration<CambioRegistrado>
    {
        public void Configure(EntityTypeBuilder<CambioRegistrado> builder)
        {
            builder.ToTable("HistorialCambios");
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).ValueGeneratedNever();
            builder.Property(c => c.Entidad).IsRequired().HasMaxLength(20);
            builder.Property(c => c.EntidadId);
            builder.Property(c => c.CorreccionId);
            builder.Property(c => c.Campo).IsRequired().HasMaxLength(CambioRegistrado.CampoMaxLength);
            builder.Property(c => c.ValorAnterior).HasMaxLength(CambioRegistrado.ValorMaxLength);
            builder.Property(c => c.ValorNuevo).HasMaxLength(CambioRegistrado.ValorMaxLength);
            builder.Property(c => c.EnUtc);
            builder.Property(c => c.Motivo).IsRequired().HasMaxLength(Domain.ValueObjects.MotivoDeCambio.MaxLength);
            // The same table describes recepciones and proveedores, so EntidadId has no foreign key; the user is never deleted
            builder.HasOne<Usuario>()
                .WithMany()
                .HasForeignKey(c => c.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(c => new { c.Entidad, c.EntidadId, c.EnUtc });
        }
    }
}
