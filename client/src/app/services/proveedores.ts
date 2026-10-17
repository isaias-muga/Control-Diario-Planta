import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
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

  registrarRecepcion(proveedorId: string, recepcion: RegistrarRecepcion): Observable<RecepcionRegistrada> {
    return this.http.post<RecepcionRegistrada>(`${this.apiUrl}/${proveedorId}/recepciones`, recepcion);
  }
}
