import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ConfirmarAccionDialog, ConfirmarAccionDialogData } from '../../components/confirmar-accion-dialog/confirmar-accion-dialog';
import { ETIQUETAS_ROL, UsuarioDialog } from '../../components/usuario-dialog/usuario-dialog';
import { OperarioDialog } from '../../components/operario-dialog/operario-dialog';
import { ValorSecretoDialog, ValorSecretoDialogData } from '../../components/valor-secreto-dialog/valor-secreto-dialog';
import { Operario, Usuario } from '../../models/usuarios';
import { AuthService } from '../../services/auth';
import { UsuariosService } from '../../services/usuarios';
import { getApiErrorMessage } from '../../utils/api-error';
import { Rol } from '../../utils/jwt';

/** Admin only: who can log in, and the operarios of the reception phone with their PINs. */
@Component({
  selector: 'app-usuarios',
  imports: [MatButtonModule, MatCardModule, MatProgressSpinnerModule, MatTableModule, MatTooltipModule],
  templateUrl: './usuarios.html',
  styleUrl: './usuarios.css',
})
export class Usuarios {
  private servicio = inject(UsuariosService);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);
  protected auth = inject(AuthService);

  protected readonly columnasUsuarios = ['username', 'nombre', 'rol', 'estado', 'acciones'];
  protected readonly columnasOperarios = ['opNombre', 'opEstado', 'opAcciones'];

  usuarios = signal<Usuario[]>([]);
  operarios = signal<Operario[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  constructor() {
    this.cargar();
  }

  protected etiqueta(rol: Rol): string {
    return ETIQUETAS_ROL[rol];
  }

  cargar(): void {
    this.loading.set(true);
    this.error.set(null);
    this.servicio.getUsuarios().subscribe({
      next: (usuarios) => {
        this.usuarios.set(usuarios);
        this.cargarOperarios();
      },
      error: (err: HttpErrorResponse) => this.fallarCarga(err),
    });
  }

  private cargarOperarios(): void {
    this.servicio.getOperarios().subscribe({
      next: (operarios) => {
        this.operarios.set(operarios);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => this.fallarCarga(err),
    });
  }

  private fallarCarga(err: HttpErrorResponse): void {
    this.error.set(getApiErrorMessage(err, 'No se pudieron cargar los usuarios.'));
    this.loading.set(false);
  }

  nuevoUsuario(): void {
    this.dialog.open(UsuarioDialog, { width: '480px', maxWidth: '95vw' }).afterClosed().subscribe((creado) => {
      if (creado) {
        this.avisar('Usuario creado.');
        this.cargar();
      }
    });
  }

  nuevoOperario(): void {
    this.dialog.open(OperarioDialog, { width: '480px', maxWidth: '95vw' }).afterClosed().subscribe((creado) => {
      if (creado) {
        this.avisar('Operario creado.');
        this.cargar();
      }
    });
  }

  restablecerPassword(usuario: Usuario): void {
    const data: ValorSecretoDialogData = {
      titulo: `Restablecer la contraseña de ${usuario.username}`,
      etiqueta: 'Contraseña temporal',
      tipo: 'password',
      ayuda: 'La persona va a tener que elegir otra al entrar, y sus sesiones abiertas se cierran.',
      guardar: (valor) => this.servicio.restablecerPassword(usuario.id, valor),
    };
    this.abrirSecreto(data, 'Contraseña restablecida.');
  }

  cambiarPin(operario: Operario): void {
    const data: ValorSecretoDialogData = {
      titulo: `Nuevo PIN de ${operario.nombre}`,
      etiqueta: 'PIN',
      tipo: 'pin',
      ayuda: 'Se lo das vos: el sistema no lo puede mostrar después.',
      guardar: (valor) => this.servicio.cambiarPin(operario.id, valor),
    };
    this.abrirSecreto(data, 'PIN cambiado.');
  }

  desactivarUsuario(usuario: Usuario): void {
    this.confirmar(
      {
        titulo: `¿Desactivar a ${usuario.username}?`,
        mensaje: 'Sus sesiones se cierran ahora y no va a poder volver a entrar.',
        confirmar: 'Desactivar',
      },
      () => this.servicio.desactivarUsuario(usuario.id),
      'Usuario desactivado.',
    );
  }

  desactivarOperario(operario: Operario): void {
    this.confirmar(
      {
        titulo: `¿Desactivar a ${operario.nombre}?`,
        mensaje: 'Su PIN deja de funcionar. Las recepciones que cargó siguen a su nombre.',
        confirmar: 'Desactivar',
      },
      () => this.servicio.desactivarOperario(operario.id),
      'Operario desactivado.',
    );
  }

  private abrirSecreto(data: ValorSecretoDialogData, aviso: string): void {
    this.dialog
      .open<ValorSecretoDialog, ValorSecretoDialogData, boolean>(ValorSecretoDialog, { data, width: '480px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((guardado) => {
        if (guardado) {
          this.avisar(aviso);
          this.cargar();
        }
      });
  }

  private confirmar(data: ConfirmarAccionDialogData, accion: () => ReturnType<UsuariosService['desactivarUsuario']>, aviso: string): void {
    this.dialog
      .open<ConfirmarAccionDialog, ConfirmarAccionDialogData, boolean>(ConfirmarAccionDialog, { data, role: 'alertdialog', width: '420px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((confirmado) => {
        if (!confirmado) {
          return;
        }
        accion().subscribe({
          next: () => {
            this.avisar(aviso);
            this.cargar();
          },
          error: (err: HttpErrorResponse) => this.avisar(getApiErrorMessage(err, 'No se pudo completar la acción.')),
        });
      });
  }

  private avisar(mensaje: string): void {
    this.snackBar.open(mensaje, 'Cerrar', { duration: 4000 });
  }
}
