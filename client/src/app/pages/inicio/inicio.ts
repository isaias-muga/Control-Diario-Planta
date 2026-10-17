import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';

/** The home of the reception phone: one big button. */
@Component({
  selector: 'app-inicio',
  imports: [RouterLink, MatButtonModule],
  templateUrl: './inicio.html',
  styleUrl: './inicio.css',
})
export class Inicio {}
