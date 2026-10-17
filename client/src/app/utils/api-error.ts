import { HttpErrorResponse } from '@angular/common/http';

/** The field an API 400 refers to ({ error, field }), if any. */
export function getApiErrorField(err: HttpErrorResponse): string | null {
  return err.status === 400 && typeof err.error?.field === 'string' ? err.error.field : null;
}

export function getApiErrorMessage(err: HttpErrorResponse, fallback: string): string {
  if (err.status === 0) {
    return 'No se pudo conectar con el servidor.';
  }

  if ((err.status === 409 || err.status === 429 || err.status === 403) && typeof err.error?.error === 'string') {
    // Conflict (e.g. duplicate ticket), too many attempts or not allowed, from the API: { error: "..." }
    return err.error.error;
  }

  if (err.status === 400) {
    // Domain validation, from ExceptionHandlingMiddleware: { error: "..." }
    if (typeof err.error?.error === 'string') {
      return err.error.error;
    }

    // ASP.NET model validation: { errors: { field: ["..."] } }
    const errors = err.error?.errors as Record<string, string[]> | undefined;
    if (errors) {
      const first = Object.values(errors)[0];
      if (first?.length) {
        return first[0];
      }
    }
  }

  return fallback;
}
