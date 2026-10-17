using System.Linq.Expressions;
using DairyControl.Application.DTOs;
using DairyControl.Application.Queries;
using DairyControl.Domain.Entities;
using DairyControl.Domain.Rules;
using DairyControl.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace DairyControl.Infrastructure.Persistence.Queries
{
    public class RecepcionQueries : IRecepcionQueries
    {
        private readonly AppDbContext _context;

        public RecepcionQueries(AppDbContext context)
        {
            _context = context;
        }

        public async Task<ResumenRecepcionesDto> GetResumenAsync(DateOnly desde, DateOnly hasta, Guid? proveedorId)
        {
            var dias = hasta.DayNumber - desde.DayNumber + 1;
            var desdeAnterior = desde.AddDays(-dias);
            var hastaAnterior = desde.AddDays(-1);

            var recepciones = Recepciones(desde, hasta, proveedorId);
            var limites = await LeerLimitesAsync();
            var fueraDeRango = limites.FueraDeRango<FilaRecepcion>(x => x.Acidez, x => x.Temperatura, x => x.Agua);

            var totales = await recepciones
                .GroupBy(x => 1)
                .Select(g => new TotalesResumenDto
                {
                    Litros = g.Sum(x => x.Litros),
                    CantidadRecepciones = g.Count(),
                    LitrosPromedioPorRecepcion = g.Average(x => x.Litros),
                    TemperaturaPromedio = g.Average(x => x.Temperatura),
                    TemperaturaMinima = g.Min(x => x.Temperatura),
                    TemperaturaMaxima = g.Max(x => x.Temperatura),
                    AcidezPromedio = g.Average(x => x.Acidez),
                    AcidezMinima = g.Min(x => x.Acidez),
                    AcidezMaxima = g.Max(x => x.Acidez),
                    GrasaPromedio = g.Average(x => x.Grasa),
                    // Count(predicate) on a nullable owned column does not translate; a conditional sum does
                    CantidadConGrasa = g.Sum(x => x.Grasa != null ? 1 : 0)
                })
                .FirstOrDefaultAsync() ?? new TotalesResumenDto();

            var proveedores = await recepciones
                .GroupBy(x => new { x.ProveedorId, x.Nombre })
                .Select(g => new ResumenProveedorDto
                {
                    ProveedorId = g.Key.ProveedorId,
                    Nombre = g.Key.Nombre,
                    Litros = g.Sum(x => x.Litros),
                    CantidadRecepciones = g.Count(),
                    AcidezPromedio = g.Average(x => x.Acidez)
                })
                .OrderByDescending(x => x.Litros)
                .ToListAsync();

            // The out-of-range rule lives in the domain as an expression, so it is applied with Where
            // (it cannot be inlined inside a grouping aggregate).
            var fueraPorProveedor = await recepciones
                .Where(fueraDeRango)
                .GroupBy(x => x.ProveedorId)
                .Select(g => new { ProveedorId = g.Key, Cantidad = g.Count() })
                .ToDictionaryAsync(x => x.ProveedorId, x => x.Cantidad);

            foreach (var proveedor in proveedores)
            {
                proveedor.CantidadFueraDeRango = fueraPorProveedor.GetValueOrDefault(proveedor.ProveedorId);
            }
            totales.CantidadFueraDeRango = fueraPorProveedor.Values.Sum();

            var porDia = await recepciones
                .GroupBy(x => x.FechaHora.Date)
                .Select(g => new
                {
                    Fecha = g.Key,
                    Litros = g.Sum(x => x.Litros),
                    Cantidad = g.Count()
                })
                .OrderBy(x => x.Fecha)
                .ToListAsync();

            totales.LitrosPeriodoAnterior = await Recepciones(desdeAnterior, hastaAnterior, proveedorId)
                .SumAsync(x => x.Litros);

            return new ResumenRecepcionesDto
            {
                Desde = desde,
                Hasta = hasta,
                DesdeAnterior = desdeAnterior,
                HastaAnterior = hastaAnterior,
                Totales = totales,
                Proveedores = proveedores,
                Dias = porDia.Select(d => new ResumenDiaDto
                {
                    Fecha = DateOnly.FromDateTime(d.Fecha),
                    Litros = d.Litros,
                    CantidadRecepciones = d.Cantidad
                }).ToList()
            };
        }

        public async Task<ListadoRecepcionesDto> GetListadoAsync(FiltroRecepciones filtro)
        {
            var limites = await LeerLimitesAsync();
            var fueraDeRango = limites.FueraDeRango<FilaListado>(x => x.Acidez, x => x.Temperatura, x => x.Agua);
            // Compiled once for the page, not once per row
            var motivosDe = MotivosDe(limites);

            var recepciones = RecepcionesListado(filtro.Desde, filtro.Hasta, filtro.ProveedorId);
            if (filtro.Ticket is int ticket)
            {
                recepciones = recepciones.Where(x => x.Ticket == ticket);
            }
            if (filtro.SoloFueraDeRango)
            {
                recepciones = recepciones.Where(fueraDeRango);
            }

            // One extra row tells whether there is a next page, without a separate count.
            var filas = await recepciones
                .OrderByDescending(x => x.FechaHora)
                .ThenByDescending(x => x.Ticket)
                .ThenByDescending(x => x.Id) // total order: equal time and ticket (or no ticket) must not shuffle between pages
                .Skip((filtro.Pagina - 1) * FiltroRecepciones.TamanoPagina)
                .Take(FiltroRecepciones.TamanoPagina + 1)
                .ToListAsync();
            var hayMas = filas.Count > FiltroRecepciones.TamanoPagina;
            if (hayMas)
            {
                filas.RemoveAt(filas.Count - 1);
            }
            var quienCargo = await QuienCargoAsync(filas.Select(f => f.Id).ToList());

            var totales = await recepciones
                .GroupBy(x => 1)
                .Select(g => new TotalesListadoDto
                {
                    Litros = g.Sum(x => x.Litros),
                    LitrosReales = g.Sum(x => x.LitrosReales),
                    CantidadRecepciones = g.Count(),
                    TicketMinimo = g.Min(x => x.Ticket),
                    TicketMaximo = g.Max(x => x.Ticket)
                })
                .FirstOrDefaultAsync() ?? new TotalesListadoDto();
            totales.LitrosReales = Math.Round(totales.LitrosReales, ParametrosCalidad.ScaleLitros);
            totales.CantidadFueraDeRango = await recepciones.CountAsync(fueraDeRango);
            totales.FueraPorAcidez = await recepciones.CountAsync(limites.AcidezAlta<FilaListado>(x => x.Acidez));
            totales.FueraPorTemperatura = await recepciones.CountAsync(limites.TemperaturaAlta<FilaListado>(x => x.Temperatura));
            totales.FueraPorAgua = await recepciones.CountAsync(limites.AguaAlta<FilaListado>(x => x.Agua));

            return new ListadoRecepcionesDto
            {
                Items = filas.Select(f =>
                {
                    var item = AItem(f, motivosDe);
                    item.RegistradoPor = quienCargo.GetValueOrDefault(f.Id);
                    return item;
                }).ToList(),
                Totales = totales,
                Dias = await SubtotalesDeDiasAsync(recepciones, fueraDeRango, filas),
                Pagina = filtro.Pagina,
                TamanoPagina = FiltroRecepciones.TamanoPagina,
                HayMas = hayMas
            };
        }

        public async Task<int?> GetSiguienteTicketAsync()
        {
            var maximo = await _context.Recepciones
                .AsNoTracking()
                .MaxAsync(r => (int?)r.Ticket);

            // Nothing above the largest possible ticket: no suggestion rather than a wrapped (negative) number
            return maximo == int.MaxValue ? null : (maximo ?? 0) + 1;
        }

        // The limits are editable at runtime, so they are read from the database on every request.
        private async Task<LimitesCalidad> LeerLimitesAsync()
        {
            return await _context.ConfiguracionesCalidad
                .AsNoTracking()
                .Select(c => c.Limites)
                .SingleAsync();
        }

        // The reasons of the (at most 100) rows on the page use the same domain rules as the SQL filters.
        private static Func<FilaListado, List<string>> MotivosDe(LimitesCalidad limites)
        {
            var acidezAlta = limites.AcidezAlta<decimal>(x => x).Compile();
            var temperaturaAlta = limites.TemperaturaAlta<decimal>(x => x).Compile();
            var aguaAlta = limites.AguaAlta<decimal>(x => x).Compile();

            return fila =>
            {
                var motivos = new List<string>();
                if (acidezAlta(fila.Acidez))
                {
                    motivos.Add(MotivosFueraDeRango.Acidez);
                }
                if (temperaturaAlta(fila.Temperatura))
                {
                    motivos.Add(MotivosFueraDeRango.Temperatura);
                }
                if (aguaAlta(fila.Agua))
                {
                    motivos.Add(MotivosFueraDeRango.Agua);
                }
                return motivos;
            };
        }

        // Who loaded each recepción of the page, from the audit columns: three small reads by id, never a join in the list query.
        private async Task<Dictionary<Guid, string>> QuienCargoAsync(List<Guid> ids)
        {
            if (ids.Count == 0)
            {
                return [];
            }
            var auditoria = await _context.Recepciones.AsNoTracking()
                .Where(r => ids.Contains(r.Id) && r.RegistradoPorUsuarioId != null)
                .Select(r => new { r.Id, r.RegistradoPorUsuarioId, r.OperarioId })
                .ToListAsync();
            var usuarioIds = auditoria.Select(a => a.RegistradoPorUsuarioId!.Value).Distinct().ToList();
            var operarioIds = auditoria.Where(a => a.OperarioId != null).Select(a => a.OperarioId!.Value).Distinct().ToList();
            var usuarios = await _context.Usuarios.AsNoTracking().Where(u => usuarioIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nombre);
            var operarios = operarioIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await _context.Operarios.AsNoTracking().Where(o => operarioIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Nombre);

            var resultado = new Dictionary<Guid, string>();
            foreach (var a in auditoria)
            {
                if (a.OperarioId is Guid operarioId && operarios.TryGetValue(operarioId, out var operario))
                {
                    resultado[a.Id] = operario;
                }
                else if (usuarios.TryGetValue(a.RegistradoPorUsuarioId!.Value, out var usuario))
                {
                    resultado[a.Id] = usuario;
                }
            }
            return resultado;
        }

        private static RecepcionListadoDto AItem(FilaListado fila, Func<FilaListado, List<string>> motivosDe)
        {
            return new RecepcionListadoDto
            {
                Id = fila.Id,
                Ticket = fila.Ticket,
                FechaHora = fila.FechaHora,
                ProveedorId = fila.ProveedorId,
                ProveedorNombre = fila.Nombre,
                Litros = fila.Litros,
                AguaPorcentaje = fila.AguaPorcentaje,
                LitrosReales = Math.Round(fila.LitrosReales, ParametrosCalidad.ScaleLitros),
                Grasa = fila.Grasa,
                Acidez = fila.Acidez,
                Temperatura = fila.Temperatura,
                Silo = fila.Silo,
                Observaciones = fila.Observaciones,
                Motivos = motivosDe(fila)
            };
        }

        private static async Task<List<SubtotalDiaDto>> SubtotalesDeDiasAsync(
            IQueryable<FilaListado> recepciones,
            Expression<Func<FilaListado, bool>> fueraDeRango,
            List<FilaListado> pagina)
        {
            if (pagina.Count == 0)
            {
                return [];
            }

            // The first and last day of a page may continue on another page, so every subtotal is computed
            // over the whole day (within the filter) in SQL, not from the rows of the page.
            var primero = pagina.Min(f => f.FechaHora).Date;
            var fin = pagina.Max(f => f.FechaHora).Date.AddDays(1);
            var delRango = recepciones.Where(x => x.FechaHora >= primero && x.FechaHora < fin);

            var porDia = await delRango
                .GroupBy(x => x.FechaHora.Date)
                .Select(g => new { Fecha = g.Key, Cantidad = g.Count(), Litros = g.Sum(x => x.Litros) })
                .OrderByDescending(x => x.Fecha)
                .ToListAsync();
            var fueraPorDia = await delRango
                .Where(fueraDeRango)
                .GroupBy(x => x.FechaHora.Date)
                .Select(g => new { Fecha = g.Key, Cantidad = g.Count() })
                .ToDictionaryAsync(x => x.Fecha, x => x.Cantidad);

            return porDia.Select(d => new SubtotalDiaDto
            {
                Fecha = DateOnly.FromDateTime(d.Fecha),
                CantidadRecepciones = d.Cantidad,
                Litros = d.Litros,
                CantidadFueraDeRango = fueraPorDia.GetValueOrDefault(d.Fecha)
            }).ToList();
        }

        // Flat projection of the owned recepciones (projecting the owned Parametros object does not translate).
        private IQueryable<FilaRecepcion> Recepciones(DateOnly desde, DateOnly hasta, Guid? proveedorId)
        {
            var inicio = desde.ToDateTime(TimeOnly.MinValue);
            var fin = hasta.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var recepciones = _context.Recepciones.AsNoTracking()
                .Where(r => r.FechaHora >= inicio && r.FechaHora < fin);
            if (proveedorId is Guid id)
            {
                recepciones = recepciones.Where(r => r.ProveedorId == id);
            }

            // Join first, then project: reading the owned Parametros inside the join's result selector does not translate
            // once the rows are grouped.
            return recepciones
                .Join(_context.Proveedores, r => r.ProveedorId, p => p.Id, (r, p) => new RecepcionConProveedor { Recepcion = r, Proveedor = p })
                .Select(x => new FilaRecepcion
                {
                    ProveedorId = x.Proveedor.Id,
                    Nombre = x.Proveedor.Nombre,
                    FechaHora = x.Recepcion.FechaHora,
                    Litros = x.Recepcion.Parametros.Litros,
                    Acidez = x.Recepcion.Parametros.Acidez,
                    Temperatura = x.Recepcion.Parametros.Temperatura,
                    Grasa = x.Recepcion.Parametros.Grasa,
                    Agua = x.Recepcion.Parametros.AguaPorcentaje ?? 0
                });
        }

        // The same rows plus what the list shows. A join (SelectMany with a result selector) instead of filtering
        // inside the collection, so the rows can be ordered and paged without an APPLY.
        private IQueryable<FilaListado> RecepcionesListado(DateOnly desde, DateOnly hasta, Guid? proveedorId)
        {
            var inicio = desde.ToDateTime(TimeOnly.MinValue);
            var fin = hasta.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var recepciones = _context.Recepciones.AsNoTracking()
                .Where(r => r.FechaHora >= inicio && r.FechaHora < fin);
            if (proveedorId is Guid id)
            {
                recepciones = recepciones.Where(r => r.ProveedorId == id);
            }

            // The "litros a pagar" formula is the domain's (Expandir swaps De for the expression), without the final
            // rounding: ROUND is not translated by every provider, so it is applied after the query.
            Expression<Func<RecepcionConProveedor, FilaListado>> proyeccion = x => new FilaListado
                {
                    Id = x.Recepcion.Id,
                    ProveedorId = x.Proveedor.Id,
                    Nombre = x.Proveedor.Nombre,
                    FechaHora = x.Recepcion.FechaHora,
                    Ticket = x.Recepcion.Ticket,
                    Silo = x.Recepcion.Silo,
                    Observaciones = x.Recepcion.Observaciones,
                    Litros = x.Recepcion.Parametros.Litros,
                    AguaPorcentaje = x.Recepcion.Parametros.AguaPorcentaje,
                    LitrosReales = LitrosAPagarRegla.De(x.Recepcion),
                    Acidez = x.Recepcion.Parametros.Acidez,
                    Temperatura = x.Recepcion.Parametros.Temperatura,
                    Grasa = x.Recepcion.Parametros.Grasa,
                    Agua = x.Recepcion.Parametros.AguaPorcentaje ?? 0
                };

            return recepciones
                .Join(_context.Proveedores, r => r.ProveedorId, p => p.Id, (r, p) => new RecepcionConProveedor { Recepcion = r, Proveedor = p })
                .Select(LitrosAPagarRegla.Expandir(proyeccion));
        }

        private sealed class RecepcionConProveedor
        {
            public RecepcionLeche Recepcion { get; set; } = null!;
            public Proveedor Proveedor { get; set; } = null!;
        }

        private class FilaRecepcion
        {
            public Guid ProveedorId { get; set; }
            public string Nombre { get; set; } = null!;
            public DateTime FechaHora { get; set; }
            public decimal Litros { get; set; }
            public decimal Acidez { get; set; }
            public decimal Temperatura { get; set; }
            public decimal? Grasa { get; set; }
            public decimal Agua { get; set; }
        }

        private sealed class FilaListado : FilaRecepcion
        {
            public Guid Id { get; set; }
            public int? Ticket { get; set; }
            public int? Silo { get; set; }
            public string? Observaciones { get; set; }
            public decimal? AguaPorcentaje { get; set; }
            public decimal LitrosReales { get; set; }
        }
    }
}
