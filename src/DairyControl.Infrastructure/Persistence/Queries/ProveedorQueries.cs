using DairyControl.Application.DTOs;
using DairyControl.Application.Queries;
using DairyControl.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DairyControl.Infrastructure.Persistence.Queries
{
    public class ProveedorQueries : IProveedorQueries
    {
        private readonly AppDbContext _context;

        public ProveedorQueries(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<ProveedorDto>> GetAllAsync()
        {
            var filas = await Proveedores().ToListAsync();
            return filas.Select(ADto).ToList();
        }

        public async Task<ProveedorDto?> GetByIdAsync(Guid id)
        {
            var fila = await Proveedores().Where(p => p.Id == id).FirstOrDefaultAsync();
            return fila is null ? null : ADto(fila);
        }

        // The counts are computed by the database; no recepción is materialized. Voided recepciones do not count.
        private IQueryable<FilaProveedor> Proveedores()
        {
            var vigente = RecepcionLeche.Vigente;
            return _context.Proveedores
                .AsNoTracking()
                .Select(p => new FilaProveedor
                {
                    Id = p.Id,
                    Nombre = p.Nombre,
                    Activo = p.Activo,
                    Version = EF.Property<byte[]>(p, "RowVersion"),
                    CantidadRecepciones = _context.Recepciones.Where(vigente).Count(r => r.ProveedorId == p.Id)
                });
        }

        private static ProveedorDto ADto(FilaProveedor fila) => new()
        {
            Id = fila.Id,
            Nombre = fila.Nombre,
            Activo = fila.Activo,
            CantidadRecepciones = fila.CantidadRecepciones,
            Version = fila.Version is null ? null : Convert.ToBase64String(fila.Version)
        };

        private sealed class FilaProveedor
        {
            public Guid Id { get; set; }
            public string Nombre { get; set; } = null!;
            public bool Activo { get; set; }
            public byte[]? Version { get; set; }
            public int CantidadRecepciones { get; set; }
        }
    }
}
