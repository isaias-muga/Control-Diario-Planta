import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Observable, of, throwError } from 'rxjs';
import { ValorSecretoDialog, ValorSecretoDialogData } from './valor-secreto-dialog';

describe('ValorSecretoDialog', () => {
  let dialogRef: { close: ReturnType<typeof vi.fn> };

  async function abrir(tipo: 'password' | 'pin', guardar: (v: string) => Observable<unknown> = () => of(null)) {
    dialogRef = { close: vi.fn() };
    const data: ValorSecretoDialogData = { titulo: 'Titulo', etiqueta: 'Etiqueta', tipo, guardar: vi.fn(guardar) };
    TestBed.configureTestingModule({
      providers: [{ provide: MatDialogRef, useValue: dialogRef }, { provide: MAT_DIALOG_DATA, useValue: data }],
    });
    const fixture = TestBed.createComponent(ValorSecretoDialog);
    await fixture.whenStable();
    return { fixture, component: fixture.componentInstance, data };
  }

  it('masks the value, and asks for a numeric keyboard only for a PIN', async () => {
    const { fixture } = await abrir('pin');
    const campo = fixture.nativeElement.querySelector('#valor') as HTMLInputElement;

    expect(campo.type).toBe('password');
    expect(campo.getAttribute('inputmode')).toBe('numeric');
  });

  it('does not ask for a numeric keyboard for a password', async () => {
    const { fixture } = await abrir('password');

    expect((fixture.nativeElement.querySelector('#valor') as HTMLInputElement).getAttribute('inputmode')).toBeNull();
  });

  it.each([
    ['pin', '123'],
    ['pin', 'abcd'],
    ['password', 'corta'],
    ['password', ''],
  ] as const)('does not send a %s of "%s"', async (tipo, valor) => {
    const { component, data } = await abrir(tipo);
    component.form.setValue({ valor });

    component.guardar();

    expect(data.guardar).not.toHaveBeenCalled();
  });

  it('sends a valid PIN once and closes with true', async () => {
    const { component, data } = await abrir('pin');
    component.form.setValue({ valor: '4321' });

    component.guardar();

    expect(data.guardar).toHaveBeenCalledExactlyOnceWith('4321');
    expect(dialogRef.close).toHaveBeenCalledExactlyOnceWith(true);
  });

  it('shows the message of the API when saving fails and stays open', async () => {
    const error = new HttpErrorResponse({ status: 409, error: { error: 'Ese PIN ya lo usa otro operario. Elegí otro.' } });
    const { component } = await abrir('pin', () => throwError(() => error));
    component.form.setValue({ valor: '4321' });

    component.guardar();

    expect(component.saveError()).toBe('Ese PIN ya lo usa otro operario. Elegí otro.');
    expect(component.saving()).toBe(false);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });
});
