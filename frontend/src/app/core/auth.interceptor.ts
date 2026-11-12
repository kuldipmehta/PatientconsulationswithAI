import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.token();

  const shouldAttach = Boolean(token) && (req.url.startsWith('/api') || req.url.includes('/api/'));
  const cloned = shouldAttach
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(cloned).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 401 && !req.url.includes('/api/auth/login')) {
        auth.logout(true);
      }
      return throwError(() => err);
    })
  );
};
