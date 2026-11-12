import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { UserRole } from '../models/patient.models';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isAuthenticated()) {
    return true;
  }
  return router.createUrlTree(['/login']);
};

export function roleGuard(allowedRoles: UserRole[]): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (!auth.isAuthenticated()) {
      return router.createUrlTree(['/login']);
    }
    if (auth.hasAnyRole(allowedRoles)) {
      return true;
    }
    return router.createUrlTree(['/patients']);
  };
}
