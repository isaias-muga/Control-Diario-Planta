import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { Observable, Subject, of, throwError } from 'rxjs';
import { Correccion } from '../../models/correcciones';
import { HistorialDialog, HistorialDialogData } from './historial-dialog';

const correccion = (cambios: Partial<Correccion> = {}): Correccion => ({
  correccionId: 'c1',
  enUtc: '2026-10-15T15:30:00',
  usuario: 'Ana Admin',
  motivo: 'Error de tipeo en la balanza',
  cambios: [{ campo: 'litros', valorAnterior: '1000', valorNuevo: '1250.5' }],
  ...cambios,
});

describe('HistorialDialog', () => {
  let fixture: ComponentFixture<HistorialDialog>;

  async function abrir(cargar: () => Observable<Correccion[]>, extra: Partial<HistorialDialogData> = {}): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [HistorialDialog],
      providers: [{ provide: MAT_DIALOG_DATA, useValue: { titulo: 'Historial de la recepción', cargar, ...extra } }],
    }).compileComponents();
    fixture = TestBed.createComponent(HistorialDialog);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const el = (): HTMLElement => fixture.nativeElement;
  const texto = (): string => (el().textContent ?? '').replace(/\s+/g, ' ').trim();

  it('shows a loading message until the history arrives', async () => {
    const respuesta = new Subject<Correccion[]>();
    await abrir(() => respuesta);

    expect(texto()).toContain('Cargando historial…');

    respuesta.next([]);
    fixture.detectChanges();
    expect(texto()).not.toContain('Cargando historial…');
  });

  it('shows who, when (plant time), why and old to new for every changed field', async () => {
    await abrir(() => of([correccion()]), { contexto: 'Ticket 4980 · Tambo Don Pedro' });

    expect(texto()).toContain('Ticket 4980 · Tambo Don Pedro');
    expect(texto()).toContain('15/10/2026 12:30 · Ana Admin');
    expect(texto()).toContain('Motivo: Error de tipeo en la balanza');
    const cambio = el().querySelector('.cambios li') as HTMLElement;
    expect((cambio.textContent ?? '').replace(/\s+/g, ' ').trim()).toBe('Litros: 1.000 → 1.250,5');
  });

  it('lists several corrections, one per item, and several fields in one correction', async () => {
    await abrir(() =>
      of([
        correccion({ correccionId: 'c2', motivo: 'Segundo', cambios: [
          { campo: 'proveedor', valorAnterior: 'Tambo Viejo', valorNuevo: 'Tambo Nuevo' },
          { campo: 'ticket', valorAnterior: '4980', valorNuevo: '5001' },
        ] }),
        correccion({ correccionId: 'c1', motivo: 'Primero' }),
      ]),
    );

    expect(el().querySelectorAll('.correccion')).toHaveLength(2);
    expect(el().querySelectorAll('.correccion')[0].querySelectorAll('.cambios li')).toHaveLength(2);
    expect(texto()).toContain('Proveedor: Tambo Viejo → Tambo Nuevo');
    expect(texto()).toContain('Ticket: 4980 → 5001');
  });

  it('shows the void as a change of state', async () => {
    await abrir(() => of([correccion({ motivo: 'Se cargó dos veces', cambios: [{ campo: 'anulacion', valorAnterior: 'Vigente', valorNuevo: 'Anulada' }] })]));

    expect(texto()).toContain('Estado: Vigente → Anulada');
  });

  it('says so when nothing was ever changed', async () => {
    await abrir(() => of([]));

    expect(texto()).toContain('Todavía no hay cambios registrados.');
  });

  it('shows a dash for a user that no longer has a name and for values that were empty', async () => {
    await abrir(() => of([correccion({ usuario: null, cambios: [{ campo: 'observaciones', valorAnterior: null, valorNuevo: 'Nueva' }] })]));

    expect(texto()).toContain('· —');
    expect(texto()).toContain('Observaciones: — → Nueva');
  });

  it('shows a reason with HTML as plain text', async () => {
    await abrir(() => of([correccion({ motivo: '<b>negrita</b><script>alert(1)</script>' })]));

    expect(el().querySelector('b')).toBeNull();
    expect(texto()).toContain('<b>negrita</b><script>alert(1)</script>');
  });

  it('shows the message of the API when the history cannot be loaded', async () => {
    await abrir(() => throwError(() => new HttpErrorResponse({ status: 0 })));

    expect(el().querySelector('.form-error')?.textContent).toContain('No se pudo conectar con el servidor.');
  });
});
