using DairyControl.Application.DTOs;
using DairyControl.Application.Queries;
using DairyControl.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DairyControl.Infrastructure.Persistence.Queries
{
    public class HistorialQueries : IHistorialQueries
    {
        private readonly AppDbContext _context;

        public HistorialQueries(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<CorreccionDto>?> GetAsync(string entidad, Guid id)
        {
            var existe = entidad switch
            {
                CambioRegistrado.EntidadRecepcion => await _context.Recepciones.AsNoTracking().AnyAsync(r => r.Id == id),
                CambioRegistrado.EntidadProveedor => await _context.Proveedores.AsNoTracking().AnyAsync(p => p.Id == id),
                CambioRegistrado.EntidadUsuario => await _context.Usuarios.AsNoTracking().AnyAsync(u => u.Id == id),
                CambioRegistrado.EntidadOperario => await _context.Operarios.AsNoTracking().AnyAsync(o => o.Id == id),
                _ => false
            };
            if (!existe)
            {
                return null;
            }

            var cambios = await _context.HistorialCambios.AsNoTracking()
                .Where(c => c.Entidad == entidad && c.EntidadId == id)
                .OrderByDescending(c => c.EnUtc)
                .ToListAsync();
            var usuarioIds = cambios.Select(c => c.UsuarioId).Distinct().ToList();
            var usuarios = await _context.Usuarios.AsNoTracking()
                .Where(u => usuarioIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Nombre);

            // One item per correction (the fields of one correction share its id), the newest first
            return cambios
                .GroupBy(c => c.CorreccionId)
                .Select(g => g.First())
                .Select(primero => new CorreccionDto
                {
                    CorreccionId = primero.CorreccionId,
                    EnUtc = primero.EnUtc,
                    Usuario = usuarios.GetValueOrDefault(primero.UsuarioId),
                    Motivo = primero.Motivo,
                    Cambios = cambios.Where(c => c.CorreccionId == primero.CorreccionId)
                        .Select(c => new CampoCambiadoDto { Campo = c.Campo, ValorAnterior = c.ValorAnterior, ValorNuevo = c.ValorNuevo })
                        .ToList()
                })
                .OrderByDescending(c => c.EnUtc)
                .ToList();
        }
    }
}
