import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth';
import { Rol } from '../utils/jwt';

/**
 * The route is for these roles. Anyone else goes to where their role starts (the reception phone never sees the office pages).
 * This only saves the user a click: the API refuses what the role may not do, whatever the client shows.
 */
export const roleGuard =
  (...roles: Rol[]): CanActivateFn =>
  () => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.isLoggedIn()) {
      return router.createUrlTree(['/login']);
    }
    if (auth.debeCambiarPassword()) {
      return router.createUrlTree(['/cambiar-password']);
    }
    const rol = auth.rol();
    if (rol === null) {
      return router.createUrlTree(['/login']); // a token with no role this client knows
    }
    return roles.includes(rol) ? true : router.createUrlTree([auth.inicio()]);
  };

/** The page to change the password: for anyone logged in. */
export const cambioPasswordGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isLoggedIn() ? true : inject(Router).createUrlTree(['/login']);
};
