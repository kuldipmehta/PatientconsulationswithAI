import { HttpErrorResponse } from '@angular/common/http';

export function extractApiErrorMessage(err: unknown, fallback = 'An unexpected error occurred.'): string {
  if (err instanceof HttpErrorResponse) {
    if (err.status === 0) {
      return 'Unable to reach the CareSync API server. Verify the backend is running.';
    }
    if (err.status === 401) {
      return 'Your session has expired or credentials are invalid. Please sign in.';
    }
    if (err.status === 403) {
      return err.error?.message || 'Access denied: your role or doctor assignment does not permit this action.';
    }
    if (err.status === 409) {
      return err.error?.message || 'Conflict detected: the record was modified concurrently or the doctor slot is already booked.';
    }
    if (err.status === 429) {
      return 'Rate limit exceeded. Please wait a moment before retrying.';
    }
    if (err.error?.errors && Array.isArray(err.error.errors) && err.error.errors.length > 0) {
      return err.error.errors.join(' ');
    }
    if (err.error?.message) {
      return err.error.message;
    }
    return `HTTP ${err.status}: ${err.statusText || fallback}`;
  }
  if (err instanceof Error && err.message) {
    return err.message;
  }
  return fallback;
}
