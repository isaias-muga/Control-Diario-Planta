using DairyControl.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DairyControl.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Proveedor> Proveedores { get; set; }
    public DbSet<RecepcionLeche> Recepciones { get; set; }
    public DbSet<ConfiguracionCalidad> ConfiguracionesCalidad { get; set; }
    public DbSet<Usuario> Usuarios { get; set; }
    public DbSet<Operario> Operarios { get; set; }
    public DbSet<CambioRegistrado> HistorialCambios { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
