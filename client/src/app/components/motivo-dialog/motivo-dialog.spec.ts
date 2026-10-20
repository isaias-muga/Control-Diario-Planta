import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Observable, Subject, of, throwError } from 'rxjs';
import { MotivoDialog, MotivoDialogData } from './motivo-dialog';

describe('MotivoDialog', () => {
  let fixture: ComponentFixture<MotivoDialog>;
  let component: MotivoDialog;
  let cerrado: unknown[];

  async function abrir(data: Partial<MotivoDialogData> = {}): Promise<void> {
    cerrado = [];
    await TestBed.configureTestingModule({
      imports: [MotivoDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { titulo: 'Anular recepción', confirmar: 'Anular', guardar: () => of(null), ...data } },
        { provide: MatDialogRef, useValue: { close: (r: unknown) => cerrado.push(r) } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(MotivoDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const interno = (): any => component as any; // the template-only members are protected
  const el = (): HTMLElement => fixture.nativeElement;
  const texto = (): string => (el().textContent ?? '').replace(/\s+/g, ' ').trim();

  it('shows the title, the message and the reason field', async () => {
    await abrir({ mensaje: 'Queda anulada para siempre.' });

    expect(el().querySelector('h2')?.textContent).toBe('Anular recepción');
    expect(texto()).toContain('Queda anulada para siempre.');
    expect(el().querySelector('textarea')).not.toBeNull();
    expect(el().querySelector('input')).toBeNull(); // no name field unless asked for
  });

  it.each([null, '', '   ', 'abcd'])('does not do anything while the reason is %j', async (motivo) => {
    const guardar = vi.fn(() => of(null));
    await abrir({ guardar });
    interno().form.controls.motivo.setValue(motivo);

    interno().confirmar();
    fixture.detectChanges();

    expect(guardar).not.toHaveBeenCalled();
    expect(cerrado).toEqual([]);
    expect(texto()).toContain('Escribí el motivo (mínimo 5 caracteres)');
  });

  it('sends the trimmed reason and closes with true when it was saved', async () => {
    const guardar = vi.fn(() => of(null));
    await abrir({ guardar });
    interno().form.controls.motivo.setValue('  Se cargó dos veces  ');

    interno().confirmar();

    expect(guardar).toHaveBeenCalledWith('Se cargó dos veces', null);
    expect(cerrado).toEqual([true]);
  });

  it('does the change once when confirmed twice before the answer arrives', async () => {
    const respuesta = new Subject<unknown>();
    const guardar = vi.fn((): Observable<unknown> => respuesta);
    await abrir({ guardar });
    interno().form.controls.motivo.setValue('Se cargó dos veces');

    interno().confirmar();
    interno().confirmar();
    respuesta.next(null);
    respuesta.complete();

    expect(guardar).toHaveBeenCalledTimes(1);
    expect(cerrado).toEqual([true]);
  });

  it('stays open and shows the message of the API when the change is refused', async () => {
    const guardar = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 409, error: { error: 'Otro administrador cambió este registro.' } })));
    await abrir({ guardar });
    interno().form.controls.motivo.setValue('Se cargó dos veces');

    interno().confirmar();
    fixture.detectChanges();

    expect(cerrado).toEqual([]);
    expect(el().querySelector('.form-error')?.textContent).toContain('Otro administrador cambió este registro.');
    expect(interno().saving()).toBe(false); // the user can try again
  });

  it('marks the reason invalid when the API says the reason is the problem', async () => {
    const guardar = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 400, error: { error: 'El motivo es obligatorio.', field: 'motivo' } })));
    await abrir({ guardar });
    interno().form.controls.motivo.setValue('Motivo suficiente');

    interno().confirmar();
    fixture.detectChanges();

    expect(interno().form.controls.motivo.invalid).toBe(true);
  });

  it('shows plain text for HTML and SQL typed as the reason', async () => {
    const guardar = vi.fn(() => of(null));
    await abrir({ guardar });
    interno().form.controls.motivo.setValue("<img src=x onerror=alert(1)>'; DROP TABLE Recepciones;--");

    interno().confirmar();

    expect(guardar).toHaveBeenCalledWith("<img src=x onerror=alert(1)>'; DROP TABLE Recepciones;--", null);
    expect(el().querySelector('img')).toBeNull();
  });

  it('makes the confirm button red for an action that is hard to undo', async () => {
    await abrir({ peligro: true });

    expect(el().querySelector('button[type="submit"]')?.classList).toContain('peligro');
  });

  describe('with an option (the new role of a user)', () => {
    const conOpcion = {
      etiqueta: 'Rol nuevo',
      opciones: [
        { valor: 'Consulta', etiqueta: 'Consulta (solo lectura)' },
        { valor: 'Oficina', etiqueta: 'Oficina' },
      ],
    };

    it('sends the chosen option with the reason', async () => {
      const guardar = vi.fn(() => of(null));
      await abrir({ guardar, opcion: conOpcion, confirmar: 'Cambiar rol' });
      interno().form.controls.opcion.setValue('Consulta');
      interno().form.controls.motivo.setValue('Pasa a solo lectura');

      interno().confirmar();

      expect(guardar).toHaveBeenCalledWith('Pasa a solo lectura', null, 'Consulta');
      expect(cerrado).toEqual([true]);
    });

    it('does not do anything until an option is chosen', async () => {
      const guardar = vi.fn(() => of(null));
      await abrir({ guardar, opcion: conOpcion });
      interno().form.controls.motivo.setValue('Pasa a solo lectura');

      interno().confirmar();
      fixture.detectChanges();

      expect(guardar).not.toHaveBeenCalled();
      expect(texto()).toContain('Elegí una opción.');
    });
  });

  describe('with a name (correcting the name of a proveedor)', () => {
    const conNombre = { etiqueta: 'Nombre', valor: 'Tambo Don Pedro' };

    it('starts with the current name and sends the trimmed new one with the reason', async () => {
      const guardar = vi.fn(() => of(null));
      await abrir({ guardar, nombre: conNombre, confirmar: 'Corregir' });
      expect(interno().form.controls.nombre.value).toBe('Tambo Don Pedro');
      interno().form.controls.nombre.setValue('  Tambo Don Pedro S.A.  ');
      interno().form.controls.motivo.setValue('Mal escrito');

      interno().confirmar();

      expect(guardar).toHaveBeenCalledWith('Mal escrito', 'Tambo Don Pedro S.A.');
    });

    it('does not send a blank name', async () => {
      const guardar = vi.fn(() => of(null));
      await abrir({ guardar, nombre: conNombre });
      interno().form.controls.nombre.setValue('   ');
      interno().form.controls.motivo.setValue('Mal escrito');

      interno().confirmar();
      fixture.detectChanges();

      expect(guardar).not.toHaveBeenCalled();
      expect(texto()).toContain('Ingresá un nombre');
    });

    it('does not accept a name over 200 characters', async () => {
      await abrir({ nombre: conNombre });
      interno().form.controls.nombre.setValue('x'.repeat(201));

      expect(interno().form.controls.nombre.invalid).toBe(true);
    });
  });
});
