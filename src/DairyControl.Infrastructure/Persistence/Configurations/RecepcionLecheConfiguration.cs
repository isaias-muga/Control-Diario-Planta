using DairyControl.Domain.Entities;
using DairyControl.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace DairyControl.Infrastructure.Persistence.Configurations
{
    internal class RecepcionLecheConfiguration : IEntityTypeConfiguration<RecepcionLeche>
    {
        public void Configure(EntityTypeBuilder<RecepcionLeche> builder)
        {
            builder.ToTable("Recepciones");
            builder.HasKey(r => r.Id);
            builder.Property(r => r.Id).ValueGeneratedNever();
            // Recepciones are historical records (payments, traceability): deleting a proveedor must never delete them.
            builder.HasOne<Proveedor>()
                .WithMany()
                .HasForeignKey(r => r.ProveedorId)
                .OnDelete(DeleteBehavior.Restrict);
            // Audit: who registered it (the logged-in user), which operario identified with the PIN, and when (UTC).
            builder.HasOne<Usuario>()
                .WithMany()
                .HasForeignKey(r => r.RegistradoPorUsuarioId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<Operario>()
                .WithMany()
                .HasForeignKey(r => r.OperarioId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Property(r => r.RegistradoEn);
            builder.OwnsOne(r => r.Parametros, p =>
            {
                p.Property(x => x.Grasa).HasPrecision(ParametrosCalidad.Precision, ParametrosCalidad.Scale);
                p.Property(x => x.Acidez).IsRequired().HasPrecision(ParametrosCalidad.Precision, ParametrosCalidad.Scale);
                p.Property(x => x.Temperatura).IsRequired().HasPrecision(ParametrosCalidad.Precision, ParametrosCalidad.Scale);
                p.Property(x => x.AguaPorcentaje).HasPrecision(ParametrosCalidad.Precision, ParametrosCalidad.Scale);
                p.Property(x => x.Litros).IsRequired().HasPrecision(ParametrosCalidad.PrecisionLitros, ParametrosCalidad.ScaleLitros);
            });
            builder.Property(r => r.FechaHora).IsRequired();
            builder.Property(r => r.Silo);
            builder.Property(r => r.Ticket);
            builder.Property(r => r.Observaciones).HasMaxLength(RecepcionLeche.ObservacionesMaxLength);
            // The ticket is unique across the whole plant; old recepciones have none.
            builder.HasIndex(r => r.Ticket).IsUnique().HasFilter("[Ticket] IS NOT NULL");
            // Resumen and the Recepciones list always filter by date range (all proveedores, or one).
            builder.HasIndex(r => r.FechaHora);
            builder.HasIndex(r => new { r.ProveedorId, r.FechaHora });
        }
    }
}
