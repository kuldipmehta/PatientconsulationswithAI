import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { ApiService } from '../../services/api.service';
import { extractApiErrorMessage } from '../../core/http-error.util';
import {
  Consultation,
  ConsultationStatus,
  Doctor,
  DoctorAvailability,
  PatientSummary
} from '../../models/patient.models';

@Component({
  selector: 'app-consultations-workspace',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="panel" aria-labelledby="consultations-heading">
      <div class="panel-header">
        <div>
          <h2 id="consultations-heading">Consultations & Conflict-Checked Scheduling</h2>
          <p class="muted">
            Double-booking prevention and optimistic concurrency protection on clinical visits.
          </p>
        </div>
        <button type="button" class="btn btn-primary" (click)="openCreateModal()">
          + Schedule Consultation
        </button>
      </div>

      <div *ngIf="errorMessage()" class="alert alert-danger" role="alert">{{ errorMessage() }}</div>
      <div *ngIf="toastMessage()" class="alert alert-success" role="status">{{ toastMessage() }}</div>

      <div *ngIf="loading()" class="state-box" role="status">Loading consultations...</div>

      <div *ngIf="!loading() && consultations().length === 0" class="state-box">
        No consultations found for this filter.
      </div>

      <div *ngIf="!loading() && consultations().length > 0" class="table-wrap">
        <table class="data-table">
          <thead>
            <tr>
              <th>ID</th>
              <th>Patient</th>
              <th>Doctor</th>
              <th>Date (UTC)</th>
              <th>Diagnosis</th>
              <th>Status</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let c of consultations()">
              <td>#{{ c.id }}</td>
              <td>{{ c.patientName }}</td>
              <td>{{ c.doctorName }}</td>
              <td>{{ c.consultationDate | date:'medium' }} ({{ c.durationMinutes }}m)</td>
              <td>{{ c.diagnosis }}</td>
              <td><span class="badge badge-primary">{{ c.status }}</span></td>
              <td class="actions-cell">
                <button type="button" class="btn btn-secondary btn-sm" (click)="openEditModal(c)">Edit</button>
                <button
                  *ngIf="auth.canDeletePatients()"
                  type="button"
                  class="btn btn-danger btn-sm"
                  (click)="archiveConsultation(c)">
                  Archive
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <div *ngIf="showModal()" class="modal-overlay" role="dialog" aria-modal="true" aria-labelledby="cons-modal-title" (keydown.escape)="closeModal()">
      <div class="modal-card">
        <div class="modal-header">
          <h3 id="cons-modal-title">{{ editingConsultation() ? 'Edit Consultation #' + editingConsultation()!.id : 'Schedule Consultation' }}</h3>
          <button type="button" class="btn btn-secondary btn-sm" (click)="closeModal()" aria-label="Close modal">✕</button>
        </div>

        <form (ngSubmit)="saveConsultation()" novalidate>
          <div class="modal-body">
            <div *ngIf="modalError()" class="alert alert-danger" role="alert">{{ modalError() }}</div>

            <div class="form-row">
              <div class="form-group" *ngIf="!editingConsultation()">
                <label class="form-label" for="c-patient">Patient *</label>
                <select id="c-patient" name="patientId" class="form-select" required [(ngModel)]="formModel.patientId">
                  <option *ngFor="let p of patients()" [ngValue]="p.id">{{ p.fullName }} (#{{ p.id }})</option>
                </select>
              </div>
              <div class="form-group">
                <label class="form-label" for="c-doctor">Doctor *</label>
                <select id="c-doctor" name="doctorId" class="form-select" [(ngModel)]="formModel.doctorId" (change)="syncDoctorName()">
                  <option *ngFor="let d of doctors()" [ngValue]="d.id">{{ d.fullName }} ({{ d.specialty }})</option>
                </select>
              </div>
            </div>

            <div class="form-row">
              <div class="form-group">
                <label class="form-label" for="c-date">Date & Time (UTC) *</label>
                <input id="c-date" name="consultationDate" type="datetime-local" class="form-control" required [(ngModel)]="formModel.consultationDateLocal" />
              </div>
              <div class="form-group">
                <label class="form-label" for="c-duration">Duration (min)</label>
                <input id="c-duration" name="durationMinutes" type="number" min="10" max="240" class="form-control" [(ngModel)]="formModel.durationMinutes" />
              </div>
            </div>

            <div class="availability-box">
              <button type="button" class="btn btn-secondary btn-sm" (click)="checkDoctorSlots()">
                Check Available Slots for Selected Doctor
              </button>
              <div *ngIf="availability() as avail" class="slot-grid">
                <button
                  *ngFor="let s of avail.slots"
                  type="button"
                  class="slot-chip"
                  [class.busy]="!s.isAvailable"
                  [disabled]="!s.isAvailable"
                  (click)="pickSlot(s.startUtc)">
                  {{ s.startUtc | date:'HH:mm':'UTC' }} UTC {{ s.isAvailable ? '' : '(Booked)' }}
                </button>
              </div>
            </div>

            <div class="form-group">
              <label class="form-label" for="c-diagnosis">Diagnosis / Reason *</label>
              <input id="c-diagnosis" name="diagnosis" class="form-control" required maxlength="250" [(ngModel)]="formModel.diagnosis" />
            </div>

            <div class="form-row">
              <div class="form-group">
                <label class="form-label" for="c-status">Status</label>
                <select id="c-status" name="status" class="form-select" [(ngModel)]="formModel.status">
                  <option value="Scheduled">Scheduled</option>
                  <option value="Completed">Completed</option>
                  <option value="FollowUpRequired">FollowUpRequired</option>
                  <option value="Cancelled">Cancelled</option>
                </select>
              </div>
              <div class="form-group">
                <label class="form-label" for="c-fee">Fee</label>
                <input id="c-fee" name="fee" type="number" min="0" class="form-control" [(ngModel)]="formModel.fee" />
              </div>
            </div>

            <div class="form-group">
              <label class="form-label" for="c-symptoms">Symptoms</label>
              <textarea id="c-symptoms" name="symptoms" class="form-control" [(ngModel)]="formModel.symptoms"></textarea>
            </div>
          </div>

          <div class="modal-footer">
            <button type="button" class="btn btn-secondary" (click)="closeModal()">Cancel</button>
            <button type="submit" class="btn btn-primary" [disabled]="saving()">
              {{ saving() ? 'Saving...' : (editingConsultation() ? 'Update Consultation' : 'Book Consultation') }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [`
    .panel { background: white; border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 1.5rem; box-shadow: var(--shadow-sm); }
    .panel-header { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1.25rem; flex-wrap: wrap; }
    .muted { color: var(--text-muted); font-size: 0.85rem; }
    .state-box { padding: 2rem; text-align: center; color: var(--text-muted); background: var(--bg-page); border-radius: var(--radius-md); }
    .alert { padding: 0.75rem 1rem; border-radius: var(--radius-md); font-size: 0.85rem; margin-bottom: 1rem; }
    .alert-danger { background: var(--danger-light); color: #b91c1c; border: 1px solid #fecaca; }
    .alert-success { background: var(--success-light); color: #047857; border: 1px solid #a7f3d0; }
    .table-wrap { overflow-x: auto; }
    .data-table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
    .data-table th, .data-table td { text-align: left; padding: 0.75rem; border-bottom: 1px solid var(--border); }
    .data-table th { background: var(--bg-page); font-weight: 700; font-size: 0.75rem; color: var(--text-muted); text-transform: uppercase; }
    .actions-cell { display: flex; gap: 0.4rem; }
    .form-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 0.75rem; }
    .availability-box { margin-bottom: 1rem; padding: 0.75rem; background: var(--bg-page); border-radius: var(--radius-md); border: 1px solid var(--border); }
    .slot-grid { display: flex; flex-wrap: wrap; gap: 0.4rem; margin-top: 0.65rem; }
    .slot-chip { padding: 0.3rem 0.55rem; font-size: 0.75rem; border-radius: var(--radius-sm); border: 1px solid var(--primary); background: white; color: var(--primary); cursor: pointer; }
    .slot-chip.busy { border-color: var(--border); background: #f1f5f9; color: var(--text-muted); cursor: not-allowed; text-decoration: line-through; }
  `]
})
export class ConsultationsWorkspaceComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  readonly auth = inject(AuthService);

  readonly consultations = signal<Consultation[]>([]);
  readonly patients = signal<PatientSummary[]>([]);
  readonly doctors = signal<Doctor[]>([]);
  readonly availability = signal<DoctorAvailability | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly showModal = signal(false);
  readonly editingConsultation = signal<Consultation | null>(null);
  readonly errorMessage = signal('');
  readonly modalError = signal('');
  readonly toastMessage = signal('');

  private filterPatientId?: number;

  formModel = {
    patientId: 1,
    doctorId: 1 as number | null,
    doctorName: 'Dr. Sarah Jenkins, MD',
    consultationDateLocal: this.defaultDateLocal(),
    durationMinutes: 30,
    diagnosis: '',
    symptoms: '',
    status: 'Scheduled' as ConsultationStatus,
    fee: 150,
    rowVersion: ''
  };

  ngOnInit(): void {
    const qpId = this.route.snapshot.queryParamMap.get('patientId');
    this.filterPatientId = qpId ? Number(qpId) : undefined;
    this.loadConsultations();
    this.api.getDoctors().subscribe({ next: res => this.doctors.set(res.data || []) });
    this.api.getPatients({ pageSize: 50 }).subscribe({
      next: res => {
        const items = res.data?.items || [];
        this.patients.set(items);
        if (items.length > 0 && !this.filterPatientId) {
          this.formModel.patientId = items[0].id;
        }
      }
    });
  }

  loadConsultations(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.api.getConsultations(this.filterPatientId).subscribe({
      next: res => {
        this.consultations.set(res.data || []);
        this.loading.set(false);
      },
      error: err => {
        this.loading.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }

  openCreateModal(): void {
    this.editingConsultation.set(null);
    this.availability.set(null);
    this.modalError.set('');
    const firstDoc = this.doctors()[0];
    this.formModel = {
      patientId: this.filterPatientId || this.patients()[0]?.id || 1,
      doctorId: firstDoc?.id ?? 1,
      doctorName: firstDoc?.fullName ?? 'Dr. Sarah Jenkins, MD',
      consultationDateLocal: this.defaultDateLocal(),
      durationMinutes: 30,
      diagnosis: '',
      symptoms: '',
      status: 'Scheduled',
      fee: 150,
      rowVersion: ''
    };
    this.showModal.set(true);
  }

  openEditModal(c: Consultation): void {
    this.editingConsultation.set(c);
    this.availability.set(null);
    this.modalError.set('');
    this.formModel = {
      patientId: c.patientId,
      doctorId: c.doctorId ?? 1,
      doctorName: c.doctorName,
      consultationDateLocal: c.consultationDate.slice(0, 16),
      durationMinutes: c.durationMinutes,
      diagnosis: c.diagnosis,
      symptoms: c.symptoms || '',
      status: c.status,
      fee: c.fee ?? 150,
      rowVersion: c.rowVersion
    };
    this.showModal.set(true);
  }

  closeModal(): void {
    this.showModal.set(false);
  }

  syncDoctorName(): void {
    const doc = this.doctors().find(d => d.id === Number(this.formModel.doctorId));
    if (doc) {
      this.formModel.doctorName = doc.fullName;
    }
  }

  checkDoctorSlots(): void {
    if (!this.formModel.doctorId) return;
    const datePart = this.formModel.consultationDateLocal.slice(0, 10);
    this.api.getDoctorAvailability(this.formModel.doctorId, datePart).subscribe({
      next: res => this.availability.set(res.data),
      error: err => this.modalError.set(extractApiErrorMessage(err))
    });
  }

  pickSlot(isoUtc: string): void {
    this.formModel.consultationDateLocal = isoUtc.slice(0, 16);
  }

  saveConsultation(): void {
    this.modalError.set('');
    if (!this.formModel.diagnosis.trim()) {
      this.modalError.set('Diagnosis or reason for consultation is required.');
      return;
    }

    this.syncDoctorName();
    const isoUtc = new Date(this.formModel.consultationDateLocal + 'Z').toISOString();
    const editing = this.editingConsultation();
    this.saving.set(true);

    if (editing) {
      this.api.updateConsultation(editing.id, {
        doctorId: this.formModel.doctorId,
        doctorName: this.formModel.doctorName,
        consultationDate: isoUtc,
        durationMinutes: this.formModel.durationMinutes,
        diagnosis: this.formModel.diagnosis.trim(),
        symptoms: this.formModel.symptoms.trim() || undefined,
        status: this.formModel.status,
        fee: this.formModel.fee,
        rowVersion: this.formModel.rowVersion
      }).subscribe({
        next: res => {
          this.saving.set(false);
          this.showModal.set(false);
          this.toastMessage.set(res.message || 'Consultation updated.');
          this.loadConsultations();
        },
        error: err => {
          this.saving.set(false);
          this.modalError.set(extractApiErrorMessage(err));
        }
      });
    } else {
      this.api.createConsultation({
        patientId: this.formModel.patientId,
        doctorId: this.formModel.doctorId,
        doctorName: this.formModel.doctorName,
        consultationDate: isoUtc,
        durationMinutes: this.formModel.durationMinutes,
        diagnosis: this.formModel.diagnosis.trim(),
        symptoms: this.formModel.symptoms.trim() || undefined,
        status: this.formModel.status,
        fee: this.formModel.fee
      }).subscribe({
        next: res => {
          this.saving.set(false);
          this.showModal.set(false);
          this.toastMessage.set(res.message || 'Consultation scheduled.');
          this.loadConsultations();
        },
        error: err => {
          this.saving.set(false);
          this.modalError.set(extractApiErrorMessage(err));
        }
      });
    }
  }

  archiveConsultation(c: Consultation): void {
    if (!confirm(`Archive consultation #${c.id}?`)) return;
    this.api.deleteConsultation(c.id).subscribe({
      next: res => {
        this.toastMessage.set(res.message || 'Consultation archived.');
        this.loadConsultations();
      },
      error: err => this.errorMessage.set(extractApiErrorMessage(err))
    });
  }

  private defaultDateLocal(): string {
    const d = new Date();
    d.setUTCDate(d.getUTCDate() + 1);
    d.setUTCHours(10, 0, 0, 0);
    return d.toISOString().slice(0, 16);
  }
}
