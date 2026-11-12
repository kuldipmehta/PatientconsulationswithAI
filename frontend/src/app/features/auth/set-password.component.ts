import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { extractApiErrorMessage } from '../../core/http-error.util';

@Component({
  selector: 'app-set-password',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="auth-page" role="main">
      <section class="auth-card" aria-labelledby="set-pwd-heading">
        <h1 id="set-pwd-heading">One-Time Password Setup</h1>
        <p class="subtitle">
          Enter your single-use setup token from your onboarding email to configure your account password.
        </p>

        <div *ngIf="errorMessage()" class="alert alert-danger" role="alert">{{ errorMessage() }}</div>
        <div *ngIf="successMessage()" class="alert alert-success" role="status">
          {{ successMessage() }} <a routerLink="/login">Sign in now</a>
        </div>

        <form (ngSubmit)="submit()" novalidate>
          <div class="form-group">
            <label class="form-label" for="setup-token">One-Time Setup Token</label>
            <input
              id="setup-token"
              name="token"
              class="form-control"
              required
              [(ngModel)]="token"
              placeholder="Paste token from onboarding email"
            />
          </div>

          <div class="form-group">
            <label class="form-label" for="new-password">New Password (minimum 8 characters)</label>
            <input
              id="new-password"
              name="newPassword"
              type="password"
              class="form-control"
              required
              minlength="8"
              [(ngModel)]="newPassword"
              placeholder="At least 8 characters"
            />
          </div>

          <button type="submit" class="btn btn-primary btn-full" [disabled]="loading()">
            {{ loading() ? 'Saving...' : 'Activate Account Password' }}
          </button>
        </form>

        <p class="footer-link">
          <a routerLink="/login">Back to Sign In</a>
        </p>
      </section>
    </div>
  `,
  styles: [`
    .auth-page { min-height: calc(100vh - 120px); display: flex; align-items: center; justify-content: center; padding: 1.5rem; }
    .auth-card { width: 100%; max-width: 440px; background: white; border: 1px solid var(--border); border-radius: var(--radius-lg); box-shadow: var(--shadow-lg); padding: 2rem; }
    .subtitle { color: var(--text-muted); font-size: 0.875rem; margin: 0.4rem 0 1.25rem; }
    .btn-full { width: 100%; }
    .alert { padding: 0.75rem 1rem; border-radius: var(--radius-md); font-size: 0.85rem; margin-bottom: 1rem; }
    .alert-danger { background: var(--danger-light); color: #b91c1c; border: 1px solid #fecaca; }
    .alert-success { background: var(--success-light); color: #047857; border: 1px solid #a7f3d0; }
    .footer-link { margin-top: 1rem; text-align: center; font-size: 0.85rem; }
    .footer-link a { color: var(--primary); font-weight: 600; text-decoration: none; }
  `]
})
export class SetPasswordComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  token = '';
  newPassword = '';
  readonly loading = signal(false);
  readonly errorMessage = signal('');
  readonly successMessage = signal('');

  ngOnInit(): void {
    const qpToken = this.route.snapshot.queryParamMap.get('token');
    if (qpToken) {
      this.token = qpToken;
    }
  }

  submit(): void {
    this.errorMessage.set('');
    this.successMessage.set('');

    if (!this.token.trim() || this.newPassword.length < 8) {
      this.errorMessage.set('Please supply a valid token and a password of at least 8 characters.');
      return;
    }

    this.loading.set(true);
    this.auth.setPassword({ token: this.token.trim(), newPassword: this.newPassword }).subscribe({
      next: res => {
        this.loading.set(false);
        this.successMessage.set(res.message || 'Password updated successfully.');
      },
      error: err => {
        this.loading.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }
}
