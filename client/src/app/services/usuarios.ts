import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { Correccion } from '../models/correcciones';
import { CrearOperario, CrearUsuario, Operario, Usuario } from '../models/usuarios';
import { Rol } from '../utils/jwt';

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

  /** Every change to a user or an operario asks for a reason and goes to its history. A protected account answers 403. */
  desactivarUsuario(id: string, motivo: string): Observable<void> {
    return this.http.post<void>(`${this.usuariosUrl}/${id}/desactivar`, { motivo });
  }

  /** The user can log in again and has to choose a new password. */
  reactivarUsuario(id: string, motivo: string): Observable<void> {
    return this.http.post<void>(`${this.usuariosUrl}/${id}/reactivar`, { motivo });
  }

  cambiarRol(id: string, rol: Rol, motivo: string): Observable<Usuario> {
    return this.http.put<Usuario>(`${this.usuariosUrl}/${id}/rol`, { rol, motivo });
  }

  /** A temporary password: the user has to choose another one at the next login. */
  restablecerPassword(id: string, password: string): Observable<void> {
    return this.http.post<void>(`${this.usuariosUrl}/${id}/restablecer-password`, { password });
  }

  getHistorialUsuario(id: string): Observable<Correccion[]> {
    return this.http.get<Correccion[]>(`${this.usuariosUrl}/${id}/historial`);
  }

  getOperarios(): Observable<Operario[]> {
    return this.http.get<Operario[]>(this.operariosUrl);
  }

  crearOperario(operario: CrearOperario): Observable<Operario> {
    return this.http.post<Operario>(this.operariosUrl, operario);
  }

  desactivarOperario(id: string, motivo: string): Observable<void> {
    return this.http.post<void>(`${this.operariosUrl}/${id}/desactivar`, { motivo });
  }

  /** Its PIN works again; the API answers 409 when another active operario has the same PIN. */
  reactivarOperario(id: string, motivo: string): Observable<void> {
    return this.http.post<void>(`${this.operariosUrl}/${id}/reactivar`, { motivo });
  }

  cambiarPin(id: string, pin: string): Observable<void> {
    return this.http.put<void>(`${this.operariosUrl}/${id}/pin`, { pin });
  }

  getHistorialOperario(id: string): Observable<Correccion[]> {
    return this.http.get<Correccion[]>(`${this.operariosUrl}/${id}/historial`);
  }
}
