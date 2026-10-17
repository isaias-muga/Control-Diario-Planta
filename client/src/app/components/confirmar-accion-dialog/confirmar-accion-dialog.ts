import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

export interface ConfirmarAccionDialogData {
  titulo: string;
  mensaje: string;
  confirmar: string;
}

/** Asks before something that cannot be undone. Closes with `true` only when the user confirms. */
@Component({
  selector: 'app-confirmar-accion-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.titulo }}</h2>
    <mat-dialog-content>{{ data.mensaje }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" cdkFocusInitial [mat-dialog-close]="false">Cancelar</button>
      <button mat-flat-button type="button" [mat-dialog-close]="true">{{ data.confirmar }}</button>
    </mat-dialog-actions>
  `,
})
export class ConfirmarAccionDialog {
  protected data = inject<ConfirmarAccionDialogData>(MAT_DIALOG_DATA);
}
