import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Inicio } from './inicio';

describe('Inicio', () => {
  it('shows a single big button that opens the quick flow', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(Inicio);
    await fixture.whenStable();

    const controles = Array.from(fixture.nativeElement.querySelectorAll('a, button')) as HTMLElement[];

    expect(controles).toHaveLength(1);
    expect(controles[0].textContent?.trim()).toBe('Cargar recepción');
    expect(controles[0].getAttribute('href')).toBe('/recepciones/carga');
  });
});
