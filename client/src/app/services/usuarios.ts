import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { CrearOperario, CrearUsuario, Operario, Usuario } from '../models/usuarios';

/** Admin only: the users of the system and the operarios of the reception phone. */
@Injectable({ providedIn: 'root' })
export class UsuariosService {
  private http = inject(HttpClient);
  private usuariosUrl = `${environment.apiUrl}/Usuarios`;
  private operariosUrl = `${environment.apiUrl}/Operarios`;

  getUsuarios(): Observable<Usuario[]> {
    return this.http.get<Usuario[]>(this.usuariosUrl);
  }

  crearUsuario(usuario: CrearUsuario): Observable<Usuario> {
    return this.http.post<Usuario>(this.usuariosUrl, usuario);
  }

  desactivarUsuario(id: string): Observable<void> {
    return this.http.post<void>(`${this.usuariosUrl}/${id}/desactivar`, {});
  }

  /** A temporary password: the user has to choose another one at the next login. */
  restablecerPassword(id: string, password: string): Observable<void> {
    return this.http.post<void>(`${this.usuariosUrl}/${id}/restablecer-password`, { password });
  }

  getOperarios(): Observable<Operario[]> {
    return this.http.get<Operario[]>(this.operariosUrl);
  }

  crearOperario(operario: CrearOperario): Observable<Operario> {
    return this.http.post<Operario>(this.operariosUrl, operario);
  }

  desactivarOperario(id: string): Observable<void> {
    return this.http.post<void>(`${this.operariosUrl}/${id}/desactivar`, {});
  }

  cambiarPin(id: string, pin: string): Observable<void> {
    return this.http.put<void>(`${this.operariosUrl}/${id}/pin`, { pin });
  }
}
