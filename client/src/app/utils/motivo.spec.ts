import { FormControl } from '@angular/forms';
import { motivoSuficiente } from './motivo';

describe('motivoSuficiente', () => {
  it.each([null, '', '   ', 'abcd', '  abcd  '])('rejects %j', (valor) => {
    expect(motivoSuficiente(new FormControl(valor))).toEqual({ motivoCorto: true });
  });

  it.each(['litro', '  litro  ', 'Error de tipeo en la balanza'])('accepts %j', (valor) => {
    expect(motivoSuficiente(new FormControl(valor))).toBeNull();
  });
});
