import { Component, ElementRef, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './services/auth';
import { ServerStatusService } from './services/server-status';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatProgressSpinnerModule, MatMenuModule, MatTooltipModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected auth = inject(AuthService);
  protected serverStatus = inject(ServerStatusService);

  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Keeps the active tab visible when the tabs row scrolls inside itself (instant, so there is no visible jump). */
  protected revelarActiva(): void {
    // routerLinkActive flips the class right after it emits, so look the tab up once it is there
    queueMicrotask(() =>
      this.host.nativeElement.querySelector('.nav-link.active')?.scrollIntoView({ inline: 'nearest', block: 'nearest' }),
    );
  }
}
