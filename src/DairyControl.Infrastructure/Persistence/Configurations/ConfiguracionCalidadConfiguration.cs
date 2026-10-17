using DairyControl.Domain.Entities;
using DairyControl.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace DairyControl.Infrastructure.Persistence.Configurations
{
    internal class ConfiguracionCalidadConfiguration : IEntityTypeConfiguration<ConfiguracionCalidad>
    {
        // Initial limits come from LimitesCalidad.Predeterminados; the fixed timestamp keeps the seed deterministic.
        private static readonly DateTime ActualizadoEnInicial = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        public void Configure(EntityTypeBuilder<ConfiguracionCalidad> builder)
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).ValueGeneratedNever();
            builder.Property(c => c.ActualizadoEn).IsRequired();
            builder.Property(c => c.CantidadSilos).IsRequired().HasDefaultValue(ConfiguracionCalidad.SilosPredeterminados);
            // Concurrency token as a shadow property: the domain entity has no field for it. The repository reads it for
            // the GET and sets its original value on update, so a stale edit matches no row and is rejected.
            builder.Property<byte[]>("RowVersion").IsRowVersion();
            builder.OwnsOne(c => c.Limites, l =>
            {
                l.Property(x => x.AcidezMaxima).IsRequired().HasPrecision(5, 2);
                l.Property(x => x.TemperaturaMaxima).IsRequired().HasPrecision(5, 2);
                l.Property(x => x.AguaMaxima).IsRequired().HasPrecision(5, 2);
                l.HasData(new
                {
                    ConfiguracionCalidadId = ConfiguracionCalidad.IdUnico,
                    LimitesCalidad.Predeterminados.AcidezMaxima,
                    LimitesCalidad.Predeterminados.TemperaturaMaxima,
                    LimitesCalidad.Predeterminados.AguaMaxima
                });
            });
            builder.HasData(new
            {
                Id = ConfiguracionCalidad.IdUnico,
                CantidadSilos = ConfiguracionCalidad.SilosPredeterminados,
                ActualizadoEn = ActualizadoEnInicial
            });
        }
    }
}
