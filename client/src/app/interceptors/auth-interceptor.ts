import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth';
import { environment } from '../../environments/environment';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.getToken();
  const isApiRequest = req.url.startsWith(environment.apiUrl);

  const request =
    token && isApiRequest ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      if (token && isApiRequest) {
        if (error.status === 401) {
          // Expired, or the user was deactivated or changed the password: the session is over
          auth.logout();
        } else if (error.status === 403 && error.error?.code === 'debeCambiarPassword') {
          auth.marcarDebeCambiarPassword();
        }
      }
      return throwError(() => error);
    }),
  );
};
