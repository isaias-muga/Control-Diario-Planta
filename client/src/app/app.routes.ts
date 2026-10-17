import { Routes } from '@angular/router';
import { authGuard } from './guards/auth-guard';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./pages/login/login').then((m) => m.Login) },
  {
    path: 'proveedores',
    loadComponent: () =>
      import('./pages/proveedores-list/proveedores-list').then((m) => m.ProveedoresList),
    canActivate: [authGuard],
  },
  {
    path: 'proveedores/:id',
    loadComponent: () =>
      import('./pages/proveedor-detail/proveedor-detail').then((m) => m.ProveedorDetail),
    canActivate: [authGuard],
  },
  {
    path: 'resumen',
    loadComponent: () => import('./pages/resumen/resumen').then((m) => m.Resumen),
    canActivate: [authGuard],
  },
  {
    path: 'recepciones/carga',
    loadComponent: () => import('./pages/carga-rapida/carga-rapida').then((m) => m.CargaRapida),
    canActivate: [authGuard],
  },
  {
    path: 'recepciones',
    loadComponent: () => import('./pages/recepciones/recepciones').then((m) => m.Recepciones),
    canActivate: [authGuard],
  },
  {
    path: 'configuracion',
    loadComponent: () =>
      import('./pages/configuracion/configuracion').then((m) => m.Configuracion),
    canActivate: [authGuard],
  },
  { path: '', redirectTo: '/resumen', pathMatch: 'full' },
];
