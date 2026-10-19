import { RecepcionDetalle } from '../models/correcciones';
import { ValoresCorreccion, cambiosDeRecepcion } from './diff-recepcion';

const original: RecepcionDetalle = {
  id: 'r1',
  proveedorId: 'p1',
  proveedorNombre: 'Tambo Don Pedro',
  proveedorActivo: true,
  ticket: 4980,
  fechaHora: '2026-10-01T06:00:00',
  litros: 1000,
  aguaPorcentaje: 1,
  grasa: 3.5,
  acidez: 18,
  temperatura: 4,
  silo: 1,
  observaciones: 'Primera carga',
  anulada: false,
  version: 'AAAAAAAAAAE=',
};

const igual = (cambios: Partial<ValoresCorreccion> = {}): ValoresCorreccion => ({
  proveedorId: 'p1',
  litros: 1000,
  grasa: 3.5,
  acidez: 18,
  temperatura: 4,
  aguaPorcentaje: 1,
  silo: 1,
  ticket: 4980,
  fechaHora: '2026-10-01T06:00',
  observaciones: 'Primera carga',
  ...cambios,
});

describe('cambiosDeRecepcion', () => {
  it('lists nothing when the form still holds what the recepción has', () => {
    expect(cambiosDeRecepcion(original, igual(), 'Tambo Don Pedro')).toEqual([]);
  });

  it('shows old to new for a changed number, in es-AR format', () => {
    const cambios = cambiosDeRecepcion(original, igual({ litros: 1250.5 }), 'Tambo Don Pedro');

    expect(cambios).toEqual([{ campo: 'litros', anterior: '1.000', nuevo: '1.250,5' }]);
  });

  it('shows the proveedor by name, not by id', () => {
    const cambios = cambiosDeRecepcion(original, igual({ proveedorId: 'p2' }), 'Tambo San José');

    expect(cambios).toEqual([{ campo: 'proveedor', anterior: 'Tambo Don Pedro', nuevo: 'Tambo San José' }]);
  });

  it('lists every changed field in the order of the form', () => {
    const cambios = cambiosDeRecepcion(
      original,
      igual({ observaciones: 'Otra', silo: 2, litros: 900, ticket: 5001, fechaHora: '2026-10-02T07:30' }),
      'Tambo Don Pedro',
    );

    expect(cambios.map((c) => c.campo)).toEqual(['fechaHora', 'ticket', 'litros', 'silo', 'observaciones']);
    expect(cambios[0]).toEqual({ campo: 'fechaHora', anterior: '01/10/2026 06:00', nuevo: '02/10/2026 07:30' });
    expect(cambios[1]).toEqual({ campo: 'ticket', anterior: '4980', nuevo: '5001' }); // identifiers are never grouped
  });

  it('shows a dash for a value that was empty or is cleared', () => {
    const cambios = cambiosDeRecepcion(original, igual({ grasa: null, aguaPorcentaje: null }), 'Tambo Don Pedro');

    expect(cambios).toEqual([
      { campo: 'grasa', anterior: '3,5', nuevo: '—' },
      { campo: 'agua', anterior: '1', nuevo: '—' },
    ]);
  });

  it('treats blank observaciones like none, and ignores spaces around the text', () => {
    expect(cambiosDeRecepcion({ ...original, observaciones: null }, igual({ observaciones: '   ' }), 'x')).toEqual([]);
    expect(cambiosDeRecepcion(original, igual({ observaciones: '  Primera carga  ' }), 'x')).toEqual([]);
    expect(cambiosDeRecepcion(original, igual({ observaciones: '' }), 'x')).toEqual([
      { campo: 'observaciones', anterior: 'Primera carga', nuevo: '—' },
    ]);
  });

  it('ignores the seconds of the date, like the form does', () => {
    expect(cambiosDeRecepcion({ ...original, fechaHora: '2026-10-01T06:00:45' }, igual(), 'x')).toEqual([]);
  });

  it('shows text with HTML or SQL as plain text, untouched', () => {
    const cambios = cambiosDeRecepcion(original, igual({ observaciones: "<script>alert(1)</script>'; DROP TABLE Recepciones;--" }), 'x');

    expect(cambios[0].nuevo).toBe("<script>alert(1)</script>'; DROP TABLE Recepciones;--");
  });
});
