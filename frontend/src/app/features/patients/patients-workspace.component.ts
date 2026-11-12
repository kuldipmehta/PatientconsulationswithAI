import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { ApiService } from '../../services/api.service';
import { extractApiErrorMessage } from '../../core/http-error.util';
import { Doctor, PagedResult, PatientDetail, PatientSummary } from '../../models/patient.models';

@Component({
  selector: 'app-patients-workspace',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="workspace-grid">
      <section class="panel" aria-labelledby="patients-heading">
        <div class="panel-header">
          <div>
            <h2 id="patients-heading">Patient Directory</h2>
            <p class="muted" *ngIf="auth.isDoctor()">
              Scoped view: showing patients assigned to your physician profile.
            </p>
            <p class="muted" *ngIf="!auth.isDoctor()">
              Manage patient records, assigned clinicians, and secure onboarding.
            </p>
          </div>
          <button type="button" class="btn btn-primary" (click)="openCreateModal()">
            + Register Patient
          </button>
        </div>

        <div class="filter-bar">
          <div class="search-field">
            <label for="patient-search" class="sr-only">Search patients</label>
            <input
              id="patient-search"
              type="search"
              class="form-control"
              placeholder="Search by name or email..."
              [(ngModel)]="searchTerm"
              (keyup.enter)="loadPatients(1)"
            />
          </div>
          <div>
            <label for="gender-filter" class="sr-only">Filter by gender</label>
            <select id="gender-filter" class="form-select" [(ngModel)]="genderFilter" (change)="loadPatients(1)">
              <option value="">All Genders</option>
              <option value="Female">Female</option>
              <option value="Male">Male</option>
              <option value="Other">Other</option>
            </select>
          </div>
          <button type="button" class="btn btn-secondary" (click)="loadPatients(1)" [disabled]="loading()">
            Search
          </button>
        </div>

        <div *ngIf="errorMessage()" class="alert alert-danger" role="alert">{{ errorMessage() }}</div>
        <div *ngIf="toastMessage()" class="alert alert-success" role="status">{{ toastMessage() }}</div>

        <div *ngIf="loading()" class="state-box" role="status">Loading patient directory...</div>

        <div *ngIf="!loading() && patientsPage()?.items?.length === 0" class="state-box">
          No patient records matched your criteria.
        </div>

        <div *ngIf="!loading() && (patientsPage()?.items?.length || 0) > 0" class="patient-list">
          <article
            *ngFor="let p of patientsPage()?.items"
            class="patient-card"
            [class.selected]="selectedPatient()?.id === p.id"
          >
            <div class="patient-main" (click)="selectPatient(p.id)" tabindex="0" (keyup.enter)="selectPatient(p.id)" role="button" [attr.aria-label]="'View details for ' + p.fullName">
              <div class="avatar-circle" aria-hidden="true">{{ p.firstName[0] }}{{ p.lastName[0] }}</div>
              <div>
                <div class="patient-name">{{ p.fullName }}</div>
                <div class="muted">{{ p.age }}y • {{ p.gender }} • {{ p.email }}</div>
                <div class="meta-badges">
                  <span class="badge badge-primary" *ngIf="p.primaryDoctorName">{{ p.primaryDoctorName }}</span>
                  <span class="badge badge-secondary">{{ p.consultationCount }} consultations</span>
                </div>
              </div>
            </div>
            <div class="card-actions">
              <button type="button" class="btn btn-secondary btn-sm" (click)="openEditModal(p.id)">Edit</button>
              <button
                *ngIf="auth.canDeletePatients()"
                type="button"
                class="btn btn-danger btn-sm"
                (click)="softDeletePatient(p)">
                Archive
              </button>
            </div>
          </article>
        </div>

        <nav class="pagination" aria-label="Patient pages" *ngIf="patientsPage() as page">
          <button type="button" class="btn btn-secondary btn-sm" [disabled]="!page.hasPreviousPage" (click)="loadPatients(page.pageNumber - 1)">
            Previous
          </button>
          <span class="muted">Page {{ page.pageNumber }} of {{ page.totalPages || 1 }} ({{ page.totalCount }} total)</span>
          <button type="button" class="btn btn-secondary btn-sm" [disabled]="!page.hasNextPage" (click)="loadPatients(page.pageNumber + 1)">
            Next
          </button>
        </nav>
      </section>

      <aside class="panel detail-panel" aria-labelledby="detail-heading">
        <h2 id="detail-heading">Patient Profile & History</h2>

        <div *ngIf="detailLoading()" class="state-box" role="status">Loading patient record...</div>

        <div *ngIf="!detailLoading() && !selectedPatient()" class="state-box">
          Select a patient from the directory to view clinical notes, private profile photo, and consultations.
        </div>

        <ng-container *ngIf="!detailLoading() && selectedPatient() as d">
          <div class="detail-hero">
            <img
              *ngIf="selectedPictureSrc()"
              [src]="selectedPictureSrc()"
              [alt]="d.fullName + ' profile photo'"
              class="detail-photo"
            />
            <div *ngIf="!selectedPictureSrc()" class="detail-photo placeholder" aria-hidden="true">
              {{ d.firstName[0] }}{{ d.lastName[0] }}
            </div>
            <div>
              <h3>{{ d.fullName }}</h3>
              <p class="muted">{{ d.age }} years • {{ d.gender }}</p>
              <p class="muted">{{ d.email }} • {{ d.phoneNumber || 'No phone on file' }}</p>
              <span class="badge badge-primary" *ngIf="d.primaryDoctorName">Primary: {{ d.primaryDoctorName }}</span>
            </div>
          </div>

          <div class="detail-section">
            <h4>Clinical Notes</h4>
            <p class="notes-box">{{ d.medicalNotes || 'No baseline medical notes recorded.' }}</p>
          </div>

          <div class="detail-actions">
            <button
              *ngIf="auth.canUseAi()"
              type="button"
              class="btn btn-purple btn-sm"
              (click)="openInAiWorkspace(d.id)">
              Analyze in AI Workspace
            </button>
            <button type="button" class="btn btn-secondary btn-sm" (click)="openConsultations(d.id)">
              Manage Consultations ({{ d.consultations.length }})
            </button>
          </div>

          <div class="detail-section">
            <h4>Consultations</h4>
            <p class="muted" *ngIf="d.consultations.length === 0">No consultations recorded yet.</p>
            <div class="cons-mini-list">
              <div *ngFor="let c of d.consultations" class="cons-mini-card">
                <div class="cons-mini-top">
                  <strong>#{{ c.id }} • {{ c.diagnosis }}</strong>
                  <span class="badge badge-secondary">{{ c.status }}</span>
                </div>
                <div class="muted">{{ c.doctorName }} • {{ c.consultationDate | date:'mediumDate' }}</div>
              </div>
            </div>
          </div>
        </ng-container>
      </aside>
    </div>

    <div *ngIf="showModal()" class="modal-overlay" role="dialog" aria-modal="true" aria-labelledby="patient-modal-title" (keydown.escape)="closeModal()">
      <div class="modal-card">
        <div class="modal-header">
          <h3 id="patient-modal-title">{{ editingId() ? 'Edit Patient #' + editingId() : 'Register New Patient' }}</h3>
          <button type="button" class="btn btn-secondary btn-sm" (click)="closeModal()" aria-label="Close patient form">✕</button>
        </div>
        <form (ngSubmit)="savePatient()" #patientForm="ngForm" novalidate>
          <div class="modal-body">
            <div *ngIf="modalError()" class="alert alert-danger" role="alert">{{ modalError() }}</div>

            <div class="form-row">
              <div class="form-group">
                <label class="form-label" for="p-first">First Name *</label>
                <input id="p-first" name="firstName" class="form-control" required maxlength="50" [(ngModel)]="formModel.firstName" #fn="ngModel" />
                <span class="field-error" *ngIf="fn.invalid && formSubmitted()">First name is required.</span>
              </div>
              <div class="form-group">
                <label class="form-label" for="p-last">Last Name *</label>
                <input id="p-last" name="lastName" class="form-control" required maxlength="50" [(ngModel)]="formModel.lastName" #ln="ngModel" />
                <span class="field-error" *ngIf="ln.invalid && formSubmitted()">Last name is required.</span>
              </div>
            </div>

            <div class="form-row">
              <div class="form-group">
                <label class="form-label" for="p-email">Email *</label>
                <input id="p-email" name="email" type="email" class="form-control" required email [(ngModel)]="formModel.email" #em="ngModel" />
                <span class="field-error" *ngIf="em.invalid && formSubmitted()">Valid email is required.</span>
              </div>
              <div class="form-group">
                <label class="form-label" for="p-phone">Phone Number</label>
                <input id="p-phone" name="phoneNumber" class="form-control" maxlength="20" [(ngModel)]="formModel.phoneNumber" />
              </div>
            </div>

            <div class="form-row">
              <div class="form-group">
                <label class="form-label" for="p-age">Age *</label>
                <input id="p-age" name="age" type="number" min="0" max="130" class="form-control" required [(ngModel)]="formModel.age" />
              </div>
              <div class="form-group">
                <label class="form-label" for="p-gender">Gender *</label>
                <select id="p-gender" name="gender" class="form-select" required [(ngModel)]="formModel.gender">
                  <option value="Female">Female</option>
                  <option value="Male">Male</option>
                  <option value="Other">Other</option>
                </select>
              </div>
              <div class="form-group" *ngIf="!auth.isDoctor()">
                <label class="form-label" for="p-doctor">Primary Physician</label>
                <select id="p-doctor" name="primaryDoctorId" class="form-select" [(ngModel)]="formModel.primaryDoctorId">
                  <option [ngValue]="null">Unassigned</option>
                  <option *ngFor="let doc of doctors()" [ngValue]="doc.id">{{ doc.fullName }} ({{ doc.specialty }})</option>
                </select>
              </div>
            </div>

            <div class="form-group">
              <label class="form-label" for="p-address">Address</label>
              <input id="p-address" name="address" class="form-control" maxlength="200" [(ngModel)]="formModel.address" />
            </div>

            <div class="form-group">
              <label class="form-label" for="p-notes">Medical Notes & Allergies</label>
              <textarea id="p-notes" name="medicalNotes" class="form-control" maxlength="1000" [(ngModel)]="formModel.medicalNotes"></textarea>
            </div>

            <div class="form-group">
              <label class="form-label" for="p-picture">Profile Photo (.jpg, .png, .webp — max 2 MB, private storage)</label>
              <input id="p-picture" type="file" class="form-control" accept=".jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp" (change)="onFileSelected($event)" />
            </div>
          </div>

          <div class="modal-footer">
            <button type="button" class="btn btn-secondary" (click)="closeModal()">Cancel</button>
            <button type="submit" class="btn btn-primary" [disabled]="saving()">
              {{ saving() ? 'Saving...' : (editingId() ? 'Save Changes' : 'Create Patient') }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [`
    .workspace-grid { display: grid; grid-template-columns: 1.4fr 1fr; gap: 1.25rem; align-items: start; }
    @media (max-width: 960px) { .workspace-grid { grid-template-columns: 1fr; } }
    .panel { background: white; border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 1.25rem; box-shadow: var(--shadow-sm); }
    .panel-header { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1rem; flex-wrap: wrap; }
    .filter-bar { display: flex; gap: 0.75rem; margin-bottom: 1rem; flex-wrap: wrap; }
    .search-field { flex: 1; min-width: 200px; }
    .muted { color: var(--text-muted); font-size: 0.82rem; }
    .state-box { padding: 1.75rem; text-align: center; color: var(--text-muted); background: var(--bg-page); border-radius: var(--radius-md); }
    .alert { padding: 0.75rem 1rem; border-radius: var(--radius-md); font-size: 0.85rem; margin-bottom: 1rem; }
    .alert-danger { background: var(--danger-light); color: #b91c1c; border: 1px solid #fecaca; }
    .alert-success { background: var(--success-light); color: #047857; border: 1px solid #a7f3d0; }
    .patient-list { display: flex; flex-direction: column; gap: 0.75rem; }
    .patient-card { border: 1px solid var(--border); border-radius: var(--radius-md); padding: 0.85rem; display: flex; justify-content: space-between; align-items: center; gap: 0.75rem; transition: border-color 0.15s; }
    .patient-card.selected, .patient-card:hover { border-color: var(--primary); background: #f8fbff; }
    .patient-main { display: flex; align-items: center; gap: 0.85rem; cursor: pointer; flex: 1; }
    .avatar-circle { width: 42px; height: 42px; border-radius: 9999px; background: var(--primary-light); color: var(--primary); display: flex; align-items: center; justify-content: center; font-weight: 700; flex-shrink: 0; }
    .patient-name { font-weight: 700; }
    .meta-badges { display: flex; gap: 0.4rem; flex-wrap: wrap; margin-top: 0.35rem; }
    .card-actions { display: flex; gap: 0.4rem; }
    .pagination { display: flex; justify-content: space-between; align-items: center; margin-top: 1rem; }
    .detail-hero { display: flex; gap: 1rem; align-items: center; margin: 1rem 0; }
    .detail-photo { width: 72px; height: 72px; border-radius: 9999px; object-fit: cover; border: 2px solid var(--primary-light); }
    .detail-photo.placeholder { display: flex; align-items: center; justify-content: center; background: var(--primary-light); color: var(--primary); font-weight: 800; font-size: 1.25rem; }
    .detail-section { margin-top: 1rem; }
    .notes-box { background: var(--bg-page); border: 1px solid var(--border); padding: 0.75rem; border-radius: var(--radius-md); font-size: 0.85rem; margin-top: 0.35rem; }
    .detail-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; margin-top: 1rem; }
    .cons-mini-list { display: flex; flex-direction: column; gap: 0.5rem; margin-top: 0.5rem; }
    .cons-mini-card { border: 1px solid var(--border); border-radius: var(--radius-sm); padding: 0.6rem; font-size: 0.82rem; }
    .cons-mini-top { display: flex; justify-content: space-between; gap: 0.5rem; }
    .form-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 0.75rem; }
    .field-error { color: #b91c1c; font-size: 0.75rem; }
    .sr-only { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0,0,0,0); border: 0; }
  `]
})
export class PatientsWorkspaceComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);

  readonly patientsPage = signal<PagedResult<PatientSummary> | null>(null);
  readonly selectedPatient = signal<PatientDetail | null>(null);
  readonly selectedPictureSrc = signal<string | null>(null);
  readonly doctors = signal<Doctor[]>([]);
  readonly loading = signal(false);
  readonly detailLoading = signal(false);
  readonly saving = signal(false);
  readonly showModal = signal(false);
  readonly editingId = signal<number | null>(null);
  readonly formSubmitted = signal(false);
  readonly errorMessage = signal('');
  readonly modalError = signal('');
  readonly toastMessage = signal('');

  searchTerm = '';
  genderFilter = '';
  selectedFile: File | null = null;
  formModel = this.createEmptyFormModel();

  private activeBlobObjectUrl: string | null = null;

  ngOnInit(): void {
    this.loadDoctors();
    this.loadPatients(1);
  }

  ngOnDestroy(): void {
    this.revokeObjectUrl();
  }

  loadDoctors(): void {
    this.api.getDoctors().subscribe({
      next: res => this.doctors.set(res.data || []),
      error: () => { /* Non-fatal if doctor catalog temporarily fails */ }
    });
  }

  loadPatients(pageNumber = 1): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.api.getPatients({
      searchTerm: this.searchTerm.trim() || undefined,
      gender: this.genderFilter || undefined,
      pageNumber,
      pageSize: 10
    }).subscribe({
      next: res => {
        this.patientsPage.set(res.data);
        this.loading.set(false);
      },
      error: err => {
        this.loading.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }

  selectPatient(id: number): void {
    this.detailLoading.set(true);
    this.revokeObjectUrl();
    this.selectedPictureSrc.set(null);

    this.api.getPatientById(id).subscribe({
      next: res => {
        this.selectedPatient.set(res.data);
        this.detailLoading.set(false);
        this.resolvePicture(res.data.profilePictureUrl);
      },
      error: err => {
        this.detailLoading.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }

  openCreateModal(): void {
    this.editingId.set(null);
    this.formModel = this.createEmptyFormModel();
    this.selectedFile = null;
    this.formSubmitted.set(false);
    this.modalError.set('');
    this.showModal.set(true);
  }

  openEditModal(id: number): void {
    this.modalError.set('');
    this.formSubmitted.set(false);
    this.selectedFile = null;

    this.api.getPatientById(id).subscribe({
      next: res => {
        const d = res.data;
        this.editingId.set(d.id);
        this.formModel = {
          firstName: d.firstName,
          lastName: d.lastName,
          email: d.email,
          phoneNumber: d.phoneNumber || '',
          age: d.age,
          gender: d.gender,
          address: d.address || '',
          medicalNotes: d.medicalNotes || '',
          primaryDoctorId: d.primaryDoctorId ?? null,
          rowVersion: d.rowVersion
        };
        this.showModal.set(true);
      },
      error: err => this.errorMessage.set(extractApiErrorMessage(err))
    });
  }

  closeModal(): void {
    this.showModal.set(false);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.modalError.set('');

    if (!file) {
      this.selectedFile = null;
      return;
    }

    const allowed = ['image/jpeg', 'image/png', 'image/webp'];
    if (!allowed.includes(file.type)) {
      this.modalError.set('Unsupported file type. Only JPEG, PNG, and WebP images are permitted.');
      input.value = '';
      this.selectedFile = null;
      return;
    }

    if (file.size > 2 * 1024 * 1024) {
      this.modalError.set('Image exceeds the 2 MB maximum upload limit.');
      input.value = '';
      this.selectedFile = null;
      return;
    }

    this.selectedFile = file;
  }

  savePatient(): void {
    this.formSubmitted.set(true);
    this.modalError.set('');

    if (!this.formModel.firstName.trim() || !this.formModel.lastName.trim() || !this.formModel.email.trim()) {
      this.modalError.set('Please complete all required patient fields.');
      return;
    }

    const fd = new FormData();
    fd.append('FirstName', this.formModel.firstName.trim());
    fd.append('LastName', this.formModel.lastName.trim());
    fd.append('Email', this.formModel.email.trim());
    fd.append('Age', String(this.formModel.age));
    fd.append('Gender', this.formModel.gender);
    if (this.formModel.phoneNumber) fd.append('PhoneNumber', this.formModel.phoneNumber.trim());
    if (this.formModel.address) fd.append('Address', this.formModel.address.trim());
    if (this.formModel.medicalNotes) fd.append('MedicalNotes', this.formModel.medicalNotes.trim());
    if (this.formModel.primaryDoctorId) fd.append('PrimaryDoctorId', String(this.formModel.primaryDoctorId));
    if (this.editingId() && this.formModel.rowVersion) fd.append('RowVersion', this.formModel.rowVersion);
    if (this.selectedFile) fd.append('Picture', this.selectedFile);

    this.saving.set(true);
    const id = this.editingId();
    const obs = id ? this.api.updatePatient(id, fd) : this.api.createPatient(fd);

    obs.subscribe({
      next: res => {
        this.saving.set(false);
        this.showModal.set(false);
        this.toastMessage.set(res.message || 'Patient saved successfully.');
        this.loadPatients(this.patientsPage()?.pageNumber || 1);
        if (res.data?.id) {
          this.selectPatient(res.data.id);
        }
      },
      error: err => {
        this.saving.set(false);
        this.modalError.set(extractApiErrorMessage(err));
      }
    });
  }

  softDeletePatient(patient: PatientSummary): void {
    if (!confirm(`Archive patient ${patient.fullName}? Medical records remain preserved in the audit log.`)) {
      return;
    }

    this.api.deletePatient(patient.id).subscribe({
      next: res => {
        this.toastMessage.set(res.message || 'Patient soft-deleted.');
        if (this.selectedPatient()?.id === patient.id) {
          this.selectedPatient.set(null);
        }
        this.loadPatients(this.patientsPage()?.pageNumber || 1);
      },
      error: err => this.errorMessage.set(extractApiErrorMessage(err))
    });
  }

  openInAiWorkspace(patientId: number): void {
    this.router.navigate(['/ai-agent'], { queryParams: { patientId } });
  }

  openConsultations(patientId: number): void {
    this.router.navigate(['/consultations'], { queryParams: { patientId } });
  }

  private resolvePicture(url?: string): void {
    if (!url) return;
    if (url.startsWith('http://') || url.startsWith('https://')) {
      this.selectedPictureSrc.set(url);
      return;
    }

    this.api.fetchProtectedPictureBlob(url).subscribe({
      next: blob => {
        this.revokeObjectUrl();
        this.activeBlobObjectUrl = URL.createObjectURL(blob);
        this.selectedPictureSrc.set(this.activeBlobObjectUrl);
      },
      error: () => this.selectedPictureSrc.set(null)
    });
  }

  private revokeObjectUrl(): void {
    if (this.activeBlobObjectUrl) {
      URL.revokeObjectURL(this.activeBlobObjectUrl);
      this.activeBlobObjectUrl = null;
    }
  }

  private createEmptyFormModel() {
    return {
      firstName: '',
      lastName: '',
      email: '',
      phoneNumber: '',
      age: 35,
      gender: 'Female',
      address: '',
      medicalNotes: '',
      primaryDoctorId: null as number | null,
      rowVersion: ''
    };
  }
}
