import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { extractApiErrorMessage } from '../../core/http-error.util';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="auth-page" role="main">
      <section class="auth-card" aria-labelledby="login-heading">
        <div class="auth-header">
          <div class="auth-badge">Clinical Workspace Sign-In</div>
          <h1 id="login-heading">CareSync Clinical Portal</h1>
          <p class="auth-subtitle">
            Role-based access control for Administrators, Physicians, and Receptionists.
          </p>
        </div>

        <div *ngIf="errorMessage()" class="alert-banner alert-danger" role="alert">
          {{ errorMessage() }}
        </div>

        <form (ngSubmit)="submit()" #loginForm="ngForm" novalidate>
          <div class="form-group">
            <label class="form-label" for="login-email">Work Email</label>
            <input
              id="login-email"
              name="email"
              type="email"
              class="form-control"
              required
              email
              [(ngModel)]="email"
              #emailCtrl="ngModel"
              autocomplete="username"
              placeholder="clinician@patientportal.com"
              [attr.aria-invalid]="emailCtrl.invalid && (emailCtrl.dirty || submitted())"
            />
            <span class="field-error" *ngIf="emailCtrl.invalid && (emailCtrl.dirty || submitted())">
              Please enter a valid work email address.
            </span>
          </div>

          <div class="form-group">
            <label class="form-label" for="login-password">Password</label>
            <input
              id="login-password"
              name="password"
              type="password"
              class="form-control"
              required
              minlength="6"
              [(ngModel)]="password"
              #pwdCtrl="ngModel"
              autocomplete="current-password"
              placeholder="Enter your password"
              [attr.aria-invalid]="pwdCtrl.invalid && (pwdCtrl.dirty || submitted())"
            />
            <span class="field-error" *ngIf="pwdCtrl.invalid && (pwdCtrl.dirty || submitted())">
              Password is required (minimum 6 characters).
            </span>
          </div>

          <button type="submit" class="btn btn-primary btn-full" [disabled]="loading()">
            {{ loading() ? 'Authenticating...' : 'Sign In to Clinical Portal' }}
          </button>
        </form>

        <div class="demo-accounts" aria-label="Development quick-fill accounts">
          <p class="demo-title">Demo Role Quick-Fill (Development Seed):</p>
          <div class="demo-grid">
            <button type="button" class="btn btn-secondary btn-sm" (click)="fillDemo('admin@patientportal.com', 'Admin@12345')">
              Admin
            </button>
            <button type="button" class="btn btn-secondary btn-sm" (click)="fillDemo('sarah.jenkins@patientportal.com', 'Doctor@12345')">
              Dr. Jenkins
            </button>
            <button type="button" class="btn btn-secondary btn-sm" (click)="fillDemo('robert.sterling@patientportal.com', 'Doctor@12345')">
              Dr. Sterling
            </button>
            <button type="button" class="btn btn-secondary btn-sm" (click)="fillDemo('reception@patientportal.com', 'Reception@12345')">
              Receptionist
            </button>
          </div>
        </div>

        <p class="auth-footer-link">
          Have a one-time onboarding token?
          <a routerLink="/set-password">Set your password</a>
        </p>
      </section>
    </div>
  `,
  styles: [`
    .auth-page { min-height: calc(100vh - 120px); display: flex; align-items: center; justify-content: center; padding: 1.5rem; }
    .auth-card { width: 100%; max-width: 440px; background: white; border: 1px solid var(--border); border-radius: var(--radius-lg); box-shadow: var(--shadow-lg); padding: 2rem; }
    .auth-header { margin-bottom: 1.5rem; }
    .auth-badge { display: inline-block; font-size: 0.75rem; font-weight: 700; color: var(--primary); background: var(--primary-light); padding: 0.25rem 0.65rem; border-radius: 9999px; margin-bottom: 0.75rem; }
    .auth-subtitle { color: var(--text-muted); font-size: 0.875rem; margin-top: 0.35rem; }
    .btn-full { width: 100%; padding: 0.65rem 1rem; margin-top: 0.25rem; }
    .alert-banner { padding: 0.75rem 1rem; border-radius: var(--radius-md); font-size: 0.85rem; margin-bottom: 1rem; }
    .alert-danger { background: var(--danger-light); color: #b91c1c; border: 1px solid #fecaca; }
    .field-error { color: #b91c1c; font-size: 0.75rem; margin-top: 0.15rem; }
    .demo-accounts { margin-top: 1.5rem; padding-top: 1rem; border-top: 1px solid var(--border); }
    .demo-title { font-size: 0.75rem; color: var(--text-muted); font-weight: 600; margin-bottom: 0.5rem; }
    .demo-grid { display: flex; flex-wrap: wrap; gap: 0.5rem; }
    .auth-footer-link { margin-top: 1.25rem; text-align: center; font-size: 0.85rem; color: var(--text-muted); }
    .auth-footer-link a { color: var(--primary); font-weight: 600; text-decoration: none; }
  `]
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  email = '';
  password = '';
  readonly submitted = signal(false);
  readonly loading = signal(false);
  readonly errorMessage = signal('');

  fillDemo(email: string, password: string): void {
    this.email = email;
    this.password = password;
    this.errorMessage.set('');
  }

  submit(): void {
    this.submitted.set(true);
    this.errorMessage.set('');

    if (!this.email.trim() || this.password.length < 6) {
      return;
    }

    this.loading.set(true);
    this.auth.login({ email: this.email.trim(), password: this.password }).subscribe({
      next: () => {
        this.loading.set(false);
        this.router.navigate(['/patients']);
      },
      error: err => {
        this.loading.set(false);
        this.errorMessage.set(extractApiErrorMessage(err, 'Invalid credentials.'));
      }
    });
  }
}
