import { inject } from '@angular/core';
import { Routes } from '@angular/router';
import { cambioPasswordGuard, roleGuard } from './guards/role-guard';
import { AuthService } from './services/auth';

// Who may open what (the API enforces the same on its side). The office reads everything; Admin also changes data and manages
// users; the reception phone only has its home and the quick flow.
const oficina = roleGuard('Admin', 'Oficina');
const soloAdmin = roleGuard('Admin');
const carga = roleGuard('Admin', 'Recepcion');
const recepcion = roleGuard('Recepcion');

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./pages/login/login').then((m) => m.Login) },
  {
    path: 'cambiar-password',
    loadComponent: () =>
      import('./pages/cambiar-password/cambiar-password').then((m) => m.CambiarPassword),
    canActivate: [cambioPasswordGuard],
  },
  {
    path: 'inicio',
    loadComponent: () => import('./pages/inicio/inicio').then((m) => m.Inicio),
    canActivate: [recepcion],
  },
  {
    path: 'proveedores',
    loadComponent: () =>
      import('./pages/proveedores-list/proveedores-list').then((m) => m.ProveedoresList),
    canActivate: [oficina],
  },
  {
    path: 'proveedores/:id',
    loadComponent: () =>
      import('./pages/proveedor-detail/proveedor-detail').then((m) => m.ProveedorDetail),
    canActivate: [oficina],
  },
  {
    path: 'resumen',
    loadComponent: () => import('./pages/resumen/resumen').then((m) => m.Resumen),
    canActivate: [oficina],
  },
  {
    path: 'recepciones/carga',
    loadComponent: () => import('./pages/carga-rapida/carga-rapida').then((m) => m.CargaRapida),
    canActivate: [carga],
  },
  {
    path: 'recepciones',
    loadComponent: () => import('./pages/recepciones/recepciones').then((m) => m.Recepciones),
    canActivate: [oficina],
  },
  {
    path: 'configuracion',
    loadComponent: () =>
      import('./pages/configuracion/configuracion').then((m) => m.Configuracion),
    canActivate: [oficina],
  },
  {
    path: 'usuarios',
    loadComponent: () => import('./pages/usuarios/usuarios').then((m) => m.Usuarios),
    canActivate: [soloAdmin],
  },
  // The start page and any unknown address: wherever the role of the user starts
  { path: '', pathMatch: 'full', redirectTo: () => inject(AuthService).inicio() },
  { path: '**', redirectTo: () => inject(AuthService).inicio() },
];
