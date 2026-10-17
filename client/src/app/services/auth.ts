import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { Rol, leerToken } from '../utils/jwt';

export interface LoginResponse {
  token: string;
  debeCambiarPassword: boolean;
}

const TOKEN_KEY = 'dairycontrol_token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);
  private dialog = inject(MatDialog);
  private apiUrl = `${environment.apiUrl}/Auth`;

  private token = signal<string | null>(localStorage.getItem(TOKEN_KEY));
  /** The server says the user must choose a new password before anything else (learned from login or from a 403). */
  private debeCambiar = signal(false);
  private renovacion: ReturnType<typeof setTimeout> | null = null;

  readonly isLoggedIn = computed(() => this.token() !== null);
  private sesion = computed(() => leerToken(this.token()));
  /** The role of the logged-in user, for deciding what to show. The server enforces what is allowed. */
  readonly rol = computed<Rol | null>(() => this.sesion()?.rol ?? null);
  readonly id = computed(() => this.sesion()?.id ?? '');
  readonly nombre = computed(() => this.sesion()?.nombre ?? '');
  readonly esAdmin = computed(() => this.rol() === 'Admin');
  readonly esRecepcion = computed(() => this.rol() === 'Recepcion');
  readonly debeCambiarPassword = computed(() => this.isLoggedIn() && (this.debeCambiar() || this.sesion()?.debeCambiarPassword === true));

  /** Where each person starts: the reception phone has one button, the office has the dashboard. */
  readonly inicio = computed(() => {
    // A token without a role this client knows would loop between guards: it starts at the login page
    if (!this.isLoggedIn() || this.rol() === null) {
      return '/login';
    }
    if (this.debeCambiarPassword()) {
      return '/cambiar-password';
    }
    return this.rol() === 'Recepcion' ? '/inicio' : '/resumen';
  });

  constructor() {
    this.programarRenovacion();
  }

  login(username: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/login`, { username, password }).pipe(
      tap((response) => this.guardar(response)),
    );
  }

  /** The user chooses a new password; the API answers with a fresh token (the old sessions end). */
  cambiarPassword(passwordActual: string, passwordNueva: string): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.apiUrl}/cambiar-password`, { passwordActual, passwordNueva })
      .pipe(tap((response) => this.guardar(response)));
  }

  /** The API refused something because the password must be changed first: send the user there. */
  marcarDebeCambiarPassword(): void {
    this.debeCambiar.set(true);
    this.cancelarRenovacion(); // renewing is refused while the password is pending
    this.router.navigate(['/cambiar-password']);
  }

  logout(): void {
    this.cancelarRenovacion();
    localStorage.removeItem(TOKEN_KEY);
    this.token.set(null);
    this.debeCambiar.set(false);
    // An open dialog (e.g. a form whose request just got a 401) must not stay on top of the login page
    this.dialog.closeAll();
    this.router.navigate(['/login']);
  }

  getToken(): string | null {
    return this.token();
  }

  private guardar(response: LoginResponse): void {
    localStorage.setItem(TOKEN_KEY, response.token);
    this.token.set(response.token);
    this.debeCambiar.set(response.debeCambiarPassword);
    this.programarRenovacion();
  }

  /** Sliding session: asks for a fresh token once half of the lifetime has gone, so an active phone never gets logged out. */
  private programarRenovacion(): void {
    this.cancelarRenovacion();
    const sesion = this.sesion();
    if (!sesion || this.debeCambiarPassword()) {
      return;
    }
    const mitad = (sesion.emitido + (sesion.expira - sesion.emitido) / 2) * 1000;
    const espera = Math.max(mitad - Date.now(), 0);
    // setTimeout cannot wait more than about 24 days
    this.renovacion = setTimeout(() => this.renovar(), Math.min(espera, 2_000_000_000));
  }

  private renovar(): void {
    const sesion = this.sesion();
    if (!sesion || sesion.expira * 1000 <= Date.now()) {
      return; // already expired: the next request gets a 401 and goes to the login page
    }
    this.http.post<LoginResponse>(`${this.apiUrl}/renovar`, {}).subscribe({
      next: (response) => this.guardar(response),
      // A 401 is handled by the interceptor (back to login); anything else is retried a minute later
      error: (err: HttpErrorResponse) => {
        // Only a connection problem or a server error is worth another try; a 4xx (403, 429) will not change by asking again
        if (this.isLoggedIn() && (err.status === 0 || err.status >= 500)) {
          this.renovacion = setTimeout(() => this.renovar(), 60_000);
        }
      },
    });
  }

  private cancelarRenovacion(): void {
    if (this.renovacion !== null) {
      clearTimeout(this.renovacion);
      this.renovacion = null;
    }
  }
}
