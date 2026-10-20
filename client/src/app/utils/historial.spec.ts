import { etiquetaCampo, textoFechaHora, textoInstante, textoValor } from './historial';

describe('historial', () => {
  describe('etiquetaCampo', () => {
    it.each([
      ['litros', 'Litros'],
      ['agua', 'Agua (%)'],
      ['fechaHora', 'Fecha y hora'],
      ['anulacion', 'Estado'],
      ['nombre', 'Nombre'],
      ['activo', 'Estado'],
    ])('names %s as %s', (campo, etiqueta) => {
      expect(etiquetaCampo(campo)).toBe(etiqueta);
    });

    it('shows an unknown field as it is', () => {
      expect(etiquetaCampo('algoNuevo')).toBe('algoNuevo');
    });
  });

  describe('textoValor', () => {
    it('shows a dash when there was no value', () => {
      expect(textoValor('litros', null)).toBe('—');
      expect(textoValor('observaciones', '')).toBe('—');
    });

    it('formats the invariant numbers of the history the es-AR way', () => {
      expect(textoValor('litros', '1250.5')).toBe('1.250,5');
      expect(textoValor('acidez', '17')).toBe('17');
    });

    it('never groups the identifiers (ticket, silo)', () => {
      expect(textoValor('ticket', '4980')).toBe('4980');
      expect(textoValor('silo', '2')).toBe('2');
    });

    it('shows the date as dd/MM/yyyy HH:mm', () => {
      expect(textoValor('fechaHora', '2026-10-01T06:00:00')).toBe('01/10/2026 06:00');
    });

    it('shows text as it is', () => {
      expect(textoValor('proveedor', 'Tambo Don Pedro')).toBe('Tambo Don Pedro');
      expect(textoValor('anulacion', 'Anulada')).toBe('Anulada');
    });

    it('does not break on a number that is not one', () => {
      expect(textoValor('litros', 'abc')).toBe('abc');
    });
  });

  describe('textoInstante', () => {
    it('shows the UTC instant of the history as plant time (UTC-3)', () => {
      expect(textoInstante('2026-10-15T15:30:00')).toBe('15/10/2026 12:30');
      expect(textoInstante('2026-10-15T15:30:00Z')).toBe('15/10/2026 12:30');
    });

    it('moves to the previous day after midnight UTC', () => {
      expect(textoInstante('2026-10-16T01:05:00')).toBe('15/10/2026 22:05');
    });
  });

  describe('textoFechaHora', () => {
    it('writes a plant date and time', () => {
      expect(textoFechaHora('2026-10-01T06:05:00')).toBe('01/10/2026 06:05');
    });
  });
});
