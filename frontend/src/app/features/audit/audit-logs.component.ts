import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { AiInteractionLog, AiUsageSummary, AuditLog } from '../../models/patient.models';
import { extractApiErrorMessage } from '../../core/http-error.util';

@Component({
  selector: 'app-audit-logs',
  standalone: true,
  imports: [CommonModule, FormsModule],
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
          <button
            type="button"
            role="tab"
            class="btn btn-sm"
            [class.btn-primary]="tab() === 'usage'"
            [class.btn-secondary]="tab() !== 'usage'"
            [attr.aria-selected]="tab() === 'usage'"
            (click)="switchTab('usage')">
            AI Usage Summary
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
                <div class="muted" *ngIf="row.promptVersion">Prompt: {{ row.promptVersion }}</div>
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

      <div *ngIf="!loading() && tab() === 'usage'">
        <div class="filter-bar">
          <label class="filter-field">
            <span class="muted">From (UTC)</span>
            <input type="date" class="form-control" [(ngModel)]="usageFrom" />
          </label>
          <label class="filter-field">
            <span class="muted">To (UTC)</span>
            <input type="date" class="form-control" [(ngModel)]="usageTo" />
          </label>
          <button type="button" class="btn btn-primary btn-sm" (click)="loadUsageSummary()">Apply Filter</button>
          <button type="button" class="btn btn-secondary btn-sm" (click)="clearUsageFilter()">Reset</button>
        </div>

        <div *ngIf="usageSummary() as summary">
          <div class="kpi-grid">
            <div class="kpi-card">
              <div class="muted">Total Requests</div>
              <div class="kpi-value">{{ summary.totalRequests | number }}</div>
            </div>
            <div class="kpi-card">
              <div class="muted">Total Tokens</div>
              <div class="kpi-value">{{ summary.totalTokens | number }}</div>
              <div class="muted">{{ summary.totalPromptTokens | number }} in / {{ summary.totalCompletionTokens | number }} out</div>
            </div>
            <div class="kpi-card">
              <div class="muted">Total Est. Cost</div>
              <div class="kpi-value">\${{ summary.totalEstimatedCostUsd | number:'1.6-6' }}</div>
            </div>
            <div class="kpi-card">
              <div class="muted">Overall p95 Latency</div>
              <div class="kpi-value">{{ summary.overallP95LatencyMs | number }} ms</div>
            </div>
          </div>

          <div class="table-wrap">
            <table class="audit-table">
              <thead>
                <tr>
                  <th>Day (UTC)</th>
                  <th>Model</th>
                  <th>Requests</th>
                  <th>Prompt / Completion</th>
                  <th>Total Tokens</th>
                  <th>Est. Cost (USD)</th>
                  <th>p95 Latency</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let row of summary.breakdown">
                  <td>{{ row.dayUtc }}</td>
                  <td><span class="badge badge-secondary">{{ row.modelUsed }}</span></td>
                  <td>{{ row.requests | number }}</td>
                  <td class="muted">{{ row.promptTokens | number }} / {{ row.completionTokens | number }}</td>
                  <td><strong>{{ row.totalTokens | number }}</strong></td>
                  <td>\${{ row.estimatedCostUsd | number:'1.6-6' }}</td>
                  <td>{{ row.p95LatencyMs | number }} ms</td>
                </tr>
                <tr *ngIf="summary.breakdown.length === 0">
                  <td colspan="7" class="state-box">No AI usage records found for the selected date range.</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </section>
  `,
  styles: [`
    .panel { background: white; border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 1.5rem; box-shadow: var(--shadow-sm); }
    .panel-header { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1.25rem; flex-wrap: wrap; }
    .tab-bar { display: flex; gap: 0.5rem; flex-wrap: wrap; }
    .filter-bar { display: flex; align-items: flex-end; gap: 0.75rem; margin-bottom: 1rem; flex-wrap: wrap; }
    .filter-field { display: flex; flex-direction: column; gap: 0.25rem; }
    .kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 0.75rem; margin-bottom: 1.25rem; }
    .kpi-card { background: var(--bg-page); border: 1px solid var(--border); border-radius: var(--radius-md); padding: 0.75rem 1rem; }
    .kpi-value { font-size: 1.25rem; font-weight: 700; margin-top: 0.15rem; }
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

  readonly tab = signal<'data' | 'ai' | 'usage'>('data');
  readonly auditLogs = signal<AuditLog[]>([]);
  readonly aiLogs = signal<AiInteractionLog[]>([]);
  readonly usageSummary = signal<AiUsageSummary | null>(null);
  readonly loading = signal(false);
  readonly errorMessage = signal('');

  usageFrom = '';
  usageTo = '';

  ngOnInit(): void {
    this.loadDataLogs();
  }

  switchTab(target: 'data' | 'ai' | 'usage'): void {
    this.tab.set(target);
    if (target === 'data') {
      this.loadDataLogs();
    } else if (target === 'ai') {
      this.loadAiLogs();
    } else {
      this.loadUsageSummary();
    }
  }

  clearUsageFilter(): void {
    this.usageFrom = '';
    this.usageTo = '';
    this.loadUsageSummary();
  }

  loadUsageSummary(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.api.getAiUsageSummary(this.usageFrom || undefined, this.usageTo || undefined).subscribe({
      next: res => {
        this.usageSummary.set(res.data || null);
        this.loading.set(false);
      },
      error: err => {
        this.loading.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
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
