import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { EmailAuditLog } from '../../models/patient.models';
import { extractApiErrorMessage } from '../../core/http-error.util';

@Component({
  selector: 'app-email-logs',
  standalone: true,
  imports: [CommonModule],
  template: `
    <section class="panel" aria-labelledby="emails-heading">
      <div class="panel-header">
        <div>
          <h2 id="emails-heading">Dispatched Onboarding & Notification Emails</h2>
          <p class="muted">
            Audit log confirming patient onboarding notifications send one-time setup links and never expose passwords.
          </p>
        </div>
        <button type="button" class="btn btn-secondary btn-sm" (click)="load()" [disabled]="loading()">
          {{ loading() ? 'Refreshing...' : 'Refresh Logs' }}
        </button>
      </div>

      <div *ngIf="errorMessage()" class="alert alert-danger" role="alert">{{ errorMessage() }}</div>

      <div *ngIf="loading()" class="state-box" role="status">Loading email notification logs...</div>

      <div *ngIf="!loading() && logs().length === 0" class="state-box">
        No email dispatch logs recorded yet.
      </div>

      <div *ngIf="!loading() && logs().length > 0" class="email-list">
        <article *ngFor="let mail of logs()" class="email-card">
          <div class="email-top">
            <div>
              <strong>{{ mail.subject }}</strong>
              <div class="muted">To: {{ mail.recipientName }} &lt;{{ mail.recipientEmail }}&gt;</div>
            </div>
            <div class="email-meta">
              <span class="badge badge-success">{{ mail.status }}</span>
              <span class="muted">{{ mail.sentAtUtc | date:'medium' }}</span>
            </div>
          </div>
          <pre class="email-body">{{ mail.body }}</pre>
        </article>
      </div>
    </section>
  `,
  styles: [`
    .panel { background: white; border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 1.5rem; box-shadow: var(--shadow-sm); }
    .panel-header { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1.25rem; flex-wrap: wrap; }
    .muted { color: var(--text-muted); font-size: 0.85rem; }
    .state-box { padding: 2rem; text-align: center; color: var(--text-muted); background: var(--bg-page); border-radius: var(--radius-md); }
    .alert-danger { background: var(--danger-light); color: #b91c1c; border: 1px solid #fecaca; padding: 0.75rem 1rem; border-radius: var(--radius-md); margin-bottom: 1rem; }
    .email-list { display: flex; flex-direction: column; gap: 1rem; }
    .email-card { border: 1px solid var(--border); border-radius: var(--radius-md); padding: 1rem; background: var(--bg-page); }
    .email-top { display: flex; justify-content: space-between; gap: 1rem; flex-wrap: wrap; margin-bottom: 0.75rem; }
    .email-meta { display: flex; flex-direction: column; align-items: flex-end; gap: 0.25rem; }
    .email-body { white-space: pre-wrap; font-family: inherit; font-size: 0.82rem; background: white; padding: 0.75rem; border-radius: var(--radius-sm); border: 1px solid var(--border); overflow-x: auto; }
  `]
})
export class EmailLogsComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly logs = signal<EmailAuditLog[]>([]);
  readonly loading = signal(false);
  readonly errorMessage = signal('');

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.api.getEmailLogs().subscribe({
      next: res => {
        this.logs.set(res.data || []);
        this.loading.set(false);
      },
      error: err => {
        this.loading.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }
}
