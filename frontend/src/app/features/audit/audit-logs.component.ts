import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { AiInteractionLog, AuditLog } from '../../models/patient.models';
import { extractApiErrorMessage } from '../../core/http-error.util';

@Component({
  selector: 'app-audit-logs',
  standalone: true,
  imports: [CommonModule],
  template: `
    <section class="panel" aria-labelledby="audit-heading">
      <div class="panel-header">
        <div>
          <h2 id="audit-heading">Compliance Audit Trail & AI Governance Logs</h2>
          <p class="muted">
            Admin-only immutable history of patient/consultation mutations, soft deletes, and sanitized AI agent interactions.
          </p>
        </div>
        <div class="tab-bar" role="tablist" aria-label="Audit views">
          <button
            type="button"
            role="tab"
            class="btn btn-sm"
            [class.btn-primary]="tab() === 'data'"
            [class.btn-secondary]="tab() !== 'data'"
            [attr.aria-selected]="tab() === 'data'"
            (click)="switchTab('data')">
            Entity Audit Trail
          </button>
          <button
            type="button"
            role="tab"
            class="btn btn-sm"
            [class.btn-primary]="tab() === 'ai'"
            [class.btn-secondary]="tab() !== 'ai'"
            [attr.aria-selected]="tab() === 'ai'"
            (click)="switchTab('ai')">
            AI Interaction Logs
          </button>
        </div>
      </div>

      <div *ngIf="errorMessage()" class="alert alert-danger" role="alert">{{ errorMessage() }}</div>
      <div *ngIf="loading()" class="state-box" role="status">Loading audit records...</div>

      <div *ngIf="!loading() && tab() === 'data'" class="table-wrap">
        <table class="audit-table">
          <thead>
            <tr>
              <th>Timestamp (UTC)</th>
              <th>User</th>
              <th>Action</th>
              <th>Entity</th>
              <th>Details</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let row of auditLogs()">
              <td>{{ row.timestampUtc | date:'medium' }}</td>
              <td>{{ row.userEmail }} <span class="badge badge-secondary">{{ row.userRole }}</span></td>
              <td><span class="badge badge-primary">{{ row.action }}</span></td>
              <td>{{ row.entityName }} #{{ row.entityId }}</td>
              <td><code class="changes-code">{{ row.changesJson || '—' }}</code></td>
            </tr>
          </tbody>
        </table>
      </div>

      <div *ngIf="!loading() && tab() === 'ai'" class="table-wrap">
        <table class="audit-table">
          <thead>
            <tr>
              <th>Timestamp (UTC)</th>
              <th>Clinician</th>
              <th>Endpoint / Patient</th>
              <th>Model & Mode</th>
              <th>Tokens & Est. Cost</th>
              <th>Tools / Latency</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let row of aiLogs()">
              <td>{{ row.createdAtUtc | date:'medium' }}</td>
              <td>{{ row.userEmail }} <span class="badge badge-secondary">{{ row.userRole }}</span></td>
              <td>{{ row.endpoint }} <span *ngIf="row.patientId">(Patient #{{ row.patientId }})</span></td>
              <td>
                <span class="badge" [class.badge-warning]="row.isSimulated" [class.badge-success]="!row.isSimulated">
                  {{ row.isSimulated ? 'Simulated' : 'Live Gemini' }}
                </span>
                <div class="muted">{{ row.modelUsed }}</div>
              </td>
              <td>
                <ng-container *ngIf="row.totalTokens != null; else noTokens">
                  <div><strong>{{ row.totalTokens | number }}</strong> tok</div>
                  <div class="muted">{{ row.promptTokens ?? 0 | number }} in / {{ row.completionTokens ?? 0 | number }} out</div>
                  <div class="muted">
                    Cost: {{ row.estimatedCostUsd != null ? ('$' + (row.estimatedCostUsd | number:'1.6-6')) : 'N/A' }}
                  </div>
                </ng-container>
                <ng-template #noTokens>
                  <span class="muted">—</span>
                </ng-template>
              </td>
              <td>
                <div>{{ row.toolCallsJson || 'None' }}</div>
                <div class="muted">{{ row.latencyMs || row.durationMs }} ms</div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  `,
  styles: [`
    .panel { background: white; border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 1.5rem; box-shadow: var(--shadow-sm); }
    .panel-header { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1.25rem; flex-wrap: wrap; }
    .tab-bar { display: flex; gap: 0.5rem; }
    .muted { color: var(--text-muted); font-size: 0.8rem; }
    .state-box { padding: 2rem; text-align: center; color: var(--text-muted); }
    .alert-danger { background: var(--danger-light); color: #b91c1c; border: 1px solid #fecaca; padding: 0.75rem 1rem; border-radius: var(--radius-md); margin-bottom: 1rem; }
    .table-wrap { overflow-x: auto; }
    .audit-table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
    .audit-table th, .audit-table td { text-align: left; padding: 0.75rem; border-bottom: 1px solid var(--border); vertical-align: top; }
    .audit-table th { background: var(--bg-page); font-weight: 700; color: var(--text-muted); font-size: 0.75rem; text-transform: uppercase; }
    .changes-code { font-size: 0.75rem; color: var(--text-muted); word-break: break-word; max-width: 320px; display: inline-block; }
  `]
})
export class AuditLogsComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly tab = signal<'data' | 'ai'>('data');
  readonly auditLogs = signal<AuditLog[]>([]);
  readonly aiLogs = signal<AiInteractionLog[]>([]);
  readonly loading = signal(false);
  readonly errorMessage = signal('');

  ngOnInit(): void {
    this.loadDataLogs();
  }

  switchTab(target: 'data' | 'ai'): void {
    this.tab.set(target);
    if (target === 'data') {
      this.loadDataLogs();
    } else {
      this.loadAiLogs();
    }
  }

  private loadDataLogs(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.api.getAuditLogs().subscribe({
      next: res => {
        this.auditLogs.set(res.data || []);
        this.loading.set(false);
      },
      error: err => {
        this.loading.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }

  private loadAiLogs(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.api.getAiInteractionLogs().subscribe({
      next: res => {
        this.aiLogs.set(res.data || []);
        this.loading.set(false);
      },
      error: err => {
        this.loading.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }
}
