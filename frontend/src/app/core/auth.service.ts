import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import {
  ApiResponse,
  LoginRequest,
  LoginResponse,
  SetPasswordRequest,
  UserProfile,
  UserRole
} from '../models/patient.models';

const TOKEN_STORAGE_KEY = 'caresync_jwt_token';
const USER_STORAGE_KEY = 'caresync_user_profile';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly _token = signal<string | null>(this.readStoredToken());
  private readonly _user = signal<UserProfile | null>(this.readStoredUser());

  readonly token = this._token.asReadonly();
  readonly currentUser = this._user.asReadonly();
  readonly isAuthenticated = computed(() => Boolean(this._token() && this._user()));
  readonly role = computed<UserRole | null>(() => this._user()?.role ?? null);
  readonly isAdmin = computed(() => this.role() === 'Admin');
  readonly isDoctor = computed(() => this.role() === 'Doctor');
  readonly isReceptionist = computed(() => this.role() === 'Receptionist');
  readonly canUseAi = computed(() => this.role() === 'Admin' || this.role() === 'Doctor');
  readonly canDeletePatients = computed(() => this.role() === 'Admin' || this.role() === 'Doctor');
  readonly canViewEmails = computed(() => this.role() === 'Admin' || this.role() === 'Receptionist');

  login(request: LoginRequest): Observable<ApiResponse<LoginResponse>> {
    return this.http.post<ApiResponse<LoginResponse>>('/api/auth/login', request).pipe(
      tap(res => {
        if (res.success && res.data) {
          this.persistSession(res.data.accessToken, res.data.user);
        }
      })
    );
  }

  setPassword(request: SetPasswordRequest): Observable<ApiResponse<boolean>> {
    return this.http.post<ApiResponse<boolean>>('/api/auth/set-password', request);
  }

  refreshProfile(): Observable<ApiResponse<UserProfile>> {
    return this.http.get<ApiResponse<UserProfile>>('/api/auth/me').pipe(
      tap(res => {
        if (res.success && res.data) {
          this._user.set(res.data);
          sessionStorage.setItem(USER_STORAGE_KEY, JSON.stringify(res.data));
        }
      })
    );
  }

  hasAnyRole(allowedRoles: UserRole[]): boolean {
    const current = this.role();
    return current !== null && allowedRoles.includes(current);
  }

  logout(redirect = true): void {
    this._token.set(null);
    this._user.set(null);
    sessionStorage.removeItem(TOKEN_STORAGE_KEY);
    sessionStorage.removeItem(USER_STORAGE_KEY);
    if (redirect) {
      this.router.navigate(['/login']);
    }
  }

  private persistSession(accessToken: string, user: UserProfile): void {
    this._token.set(accessToken);
    this._user.set(user);
    sessionStorage.setItem(TOKEN_STORAGE_KEY, accessToken);
    sessionStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
  }

  private readStoredToken(): string | null {
    try {
      return sessionStorage.getItem(TOKEN_STORAGE_KEY);
    } catch {
      return null;
    }
  }

  private readStoredUser(): UserProfile | null {
    try {
      const raw = sessionStorage.getItem(USER_STORAGE_KEY);
      return raw ? (JSON.parse(raw) as UserProfile) : null;
    } catch {
      return null;
    }
  }
}
