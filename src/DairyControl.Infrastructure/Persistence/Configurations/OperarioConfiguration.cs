using DairyControl.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DairyControl.Infrastructure.Persistence.Configurations
{
    internal class OperarioConfiguration : IEntityTypeConfiguration<Operario>
    {
        public const int PinHashMaxLength = 64; // hex of an HMAC-SHA256

        public void Configure(EntityTypeBuilder<Operario> builder)
        {
            builder.ToTable("Operarios");
            builder.HasKey(o => o.Id);
            builder.Property(o => o.Id).ValueGeneratedNever();
            builder.Property(o => o.Nombre).IsRequired().HasMaxLength(Operario.NombreMaxLength);
            builder.Property(o => o.PinHash).IsRequired().HasMaxLength(PinHashMaxLength);
            // A PIN identifies one active operario; a deactivated operario's PIN can be reused.
            builder.HasIndex(o => o.PinHash).IsUnique().HasFilter("[Activo] = 1");
        }
    }
}
