import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { Correccion } from '../models/correcciones';
import { Proveedor, ProveedorOpcion, RecepcionRegistrada, RegistrarRecepcion } from '../models/proveedor';

@Injectable({ providedIn: 'root' })
export class ProveedoresService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/Proveedores`;

  getAll(): Observable<Proveedor[]> {
    return this.http.get<Proveedor[]>(this.apiUrl);
  }

  /** Id and name only (readable by the reception phone). */
  getOpciones(): Observable<ProveedorOpcion[]> {
    return this.http.get<ProveedorOpcion[]>(`${this.apiUrl}/opciones`);
  }

  getById(id: string): Observable<Proveedor> {
    return this.http.get<Proveedor>(`${this.apiUrl}/${id}`);
  }

  create(nombre: string): Observable<Proveedor> {
    return this.http.post<Proveedor>(this.apiUrl, { nombre });
  }

  /** Corrects the name, with a reason; the version is the one of the proveedor that was read. */
  renombrar(id: string, nombre: string, motivo: string, version: string): Observable<Proveedor> {
    return this.http.put<Proveedor>(`${this.apiUrl}/${id}`, { nombre, motivo, version });
  }

  /** A proveedor is never deleted: deactivating hides it from new recepciones and keeps its history. */
  desactivar(id: string, motivo: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${id}/desactivar`, { motivo });
  }

  reactivar(id: string, motivo: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${id}/reactivar`, { motivo });
  }

  getHistorial(id: string): Observable<Correccion[]> {
    return this.http.get<Correccion[]>(`${this.apiUrl}/${id}/historial`);
  }

  registrarRecepcion(proveedorId: string, recepcion: RegistrarRecepcion): Observable<RecepcionRegistrada> {
    return this.http.post<RecepcionRegistrada>(`${this.apiUrl}/${proveedorId}/recepciones`, recepcion);
  }
}
