import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { HistorialDialog, HistorialDialogData } from '../../components/historial-dialog/historial-dialog';
import { MotivoDialog, MotivoDialogData } from '../../components/motivo-dialog/motivo-dialog';
import { ETIQUETAS_ROL, UsuarioDialog } from '../../components/usuario-dialog/usuario-dialog';
import { OperarioDialog } from '../../components/operario-dialog/operario-dialog';
import { ValorSecretoDialog, ValorSecretoDialogData } from '../../components/valor-secreto-dialog/valor-secreto-dialog';
import { Operario, Usuario } from '../../models/usuarios';
import { AuthService } from '../../services/auth';
import { UsuariosService } from '../../services/usuarios';
import { getApiErrorMessage } from '../../utils/api-error';
import { Rol } from '../../utils/jwt';

export const TEXTO_CUENTA_PROTEGIDA =
  'Cuenta protegida: otro administrador no puede desactivarla, cambiarle el rol ni restablecerle la contraseña.';

/** Admin only: who can log in, and the operarios of the reception phone with their PINs. Every change asks for a reason and has a history. */
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
  protected readonly textoCuentaProtegida = TEXTO_CUENTA_PROTEGIDA;

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

  /** The API refuses it anyway (403); the client only saves the click. A protected account can only be changed by its own user. */
  protected puedeTocar(usuario: Usuario): boolean {
    return !usuario.protegido || usuario.id === this.auth.id();
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
    this.pedirMotivo(
      {
        titulo: `¿Desactivar a ${usuario.username}?`,
        mensaje: 'Sus sesiones se cierran ahora y no va a poder entrar hasta que lo reactives.',
        confirmar: 'Desactivar',
        peligro: true,
        guardar: (motivo) => this.servicio.desactivarUsuario(usuario.id, motivo),
      },
      'Usuario desactivado.',
    );
  }

  reactivarUsuario(usuario: Usuario): void {
    this.pedirMotivo(
      {
        titulo: `¿Reactivar a ${usuario.username}?`,
        mensaje: 'Va a poder entrar de nuevo con su contraseña anterior, y tiene que elegir una nueva al entrar. Si no la recuerda, restablecésela después.',
        confirmar: 'Reactivar',
        guardar: (motivo) => this.servicio.reactivarUsuario(usuario.id, motivo),
      },
      'Usuario reactivado.',
    );
  }

  cambiarRol(usuario: Usuario): void {
    this.pedirMotivo(
      {
        titulo: `Cambiar el rol de ${usuario.username}`,
        mensaje: `Hoy es ${ETIQUETAS_ROL[usuario.rol]}. Sus sesiones abiertas se cierran y vuelve a entrar con el rol nuevo.`,
        confirmar: 'Cambiar rol',
        opcion: {
          etiqueta: 'Rol nuevo',
          opciones: (Object.keys(ETIQUETAS_ROL) as Rol[])
            .filter((rol) => rol !== usuario.rol)
            .map((rol) => ({ valor: rol, etiqueta: ETIQUETAS_ROL[rol] })),
        },
        guardar: (motivo, _nombre, rol) => this.servicio.cambiarRol(usuario.id, rol as Rol, motivo),
      },
      'Rol cambiado.',
    );
  }

  verHistorialUsuario(usuario: Usuario): void {
    this.abrirHistorial({
      titulo: `Historial de ${usuario.username}`,
      contexto: usuario.nombre,
      cargar: () => this.servicio.getHistorialUsuario(usuario.id),
    });
  }

  desactivarOperario(operario: Operario): void {
    this.pedirMotivo(
      {
        titulo: `¿Desactivar a ${operario.nombre}?`,
        mensaje: 'Su PIN deja de funcionar. Las recepciones que cargó siguen a su nombre.',
        confirmar: 'Desactivar',
        peligro: true,
        guardar: (motivo) => this.servicio.desactivarOperario(operario.id, motivo),
      },
      'Operario desactivado.',
    );
  }

  reactivarOperario(operario: Operario): void {
    this.pedirMotivo(
      {
        titulo: `¿Reactivar a ${operario.nombre}?`,
        mensaje: 'Su PIN anterior vuelve a funcionar. Si otro operario activo ya usa ese PIN, no se puede reactivar hasta que ese otro se desactive.',
        confirmar: 'Reactivar',
        guardar: (motivo) => this.servicio.reactivarOperario(operario.id, motivo),
      },
      'Operario reactivado.',
    );
  }

  verHistorialOperario(operario: Operario): void {
    this.abrirHistorial({
      titulo: `Historial de ${operario.nombre}`,
      cargar: () => this.servicio.getHistorialOperario(operario.id),
    });
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

  /** Asks for the reason and does the change; the dialog shows the API's message (403, 409...) when it fails. */
  private pedirMotivo(data: MotivoDialogData, aviso: string): void {
    this.dialog
      .open<MotivoDialog, MotivoDialogData, boolean>(MotivoDialog, { data, width: '480px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((guardado) => {
        if (guardado) {
          this.avisar(aviso);
          this.cargar();
        }
      });
  }

  private abrirHistorial(data: HistorialDialogData): void {
    this.dialog.open<HistorialDialog, HistorialDialogData>(HistorialDialog, { data, width: '560px', maxWidth: '95vw' });
  }

  private avisar(mensaje: string): void {
    this.snackBar.open(mensaje, 'Cerrar', { duration: 4000 });
  }
}
