import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from './services/api.service';
import {
  PatientSummary,
  PatientDetail,
  Consultation,
  CreateConsultationDto,
  ConsultationStatus,
  EmailAuditLog,
  LivePatientSummary,
  AiChatMessage,
  AiChatResponse,
  AiTriageResponse,
  PagedResult
} from './models/patient.models';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent implements OnInit {
  api = inject(ApiService);

  // Active navigation tab
  activeTab: 'patients' | 'consultations' | 'ai' | 'emails' | 'settings' = 'patients';

  // Backend Health
  isBackendConnected = false;
  backendChecking = false;

  // Alerts & Notifications
  alertMessage: string | null = null;
  alertType: 'success' | 'danger' | 'warning' | 'info' = 'info';

  // ===================== PATIENTS STATE =====================
  patientsPaged: PagedResult<PatientSummary> | null = null;
  loadingPatients = false;
  searchTerm = '';
  selectedGender = '';
  currentPage = 1;
  pageSize = 10;

  // Patient Modal (Create / Edit)
  showPatientModal = false;
  isEditingPatient = false;
  editingPatientId: number | null = null;
  patientForm = {
    firstName: '',
    lastName: '',
    email: '',
    phoneNumber: '',
    age: 30,
    gender: 'Female',
    dateOfBirth: '',
    address: '',
    medicalNotes: ''
  };
  selectedPictureFile: File | null = null;
  picturePreviewUrl: string | null = null;
  savingPatient = false;

  // Patient Detail Drawer / Modal
  selectedPatient: PatientDetail | null = null;
  loadingPatientDetail = false;

  // ===================== CONSULTATIONS STATE =====================
  consultations: Consultation[] = [];
  loadingConsultations = false;
  filterConsultationPatientId: number | null = null;

  // Consultation Modal
  showConsultationModal = false;
  isEditingConsultation = false;
  editingConsultationId: number | null = null;
  consultationForm: CreateConsultationDto = {
    patientId: 1,
    doctorName: 'Dr. Sarah Jenkins, MD',
    consultationDate: new Date().toISOString().substring(0, 10),
    diagnosis: '',
    symptoms: '',
    notes: '',
    treatmentPlan: '',
    prescription: '',
    status: 'Scheduled',
    fee: 150.00
  };
  savingConsultation = false;

  // ===================== AI AGENT STATE =====================
  aiSubTab: 'summary' | 'chat' | 'triage' = 'summary';
  aiModelOverride = 'gemini-flash-latest';
  aiApiKeyOverride = '';
  availableModels: string[] = [];
  loadingModels = false;

  // AI Live Summary
  summaryPatientId: number = 1;
  summaryCustomPrompt = 'Synthesize all past visits, highlight any clinical risks, and recommend actionable doctor next steps.';
  generatingSummary = false;
  liveSummaryResult: LivePatientSummary | null = null;

  // AI Chat
  chatMessage = '';
  chatContextPatientId: number | null = null;
  chatHistory: AiChatMessage[] = [];
  sendingChat = false;

  // AI Triage
  triageForm = {
    symptoms: 'Patient reports severe throbbing headache, blurred vision, and nausea since morning.',
    patientAge: 45,
    gender: 'Female',
    conditions: 'Hypertension'
  };
  runningTriage = false;
  triageResult: AiTriageResponse | null = null;

  // ===================== EMAIL AUDIT LOGS =====================
  emailLogs: EmailAuditLog[] = [];
  loadingEmails = false;
  selectedEmailBody: EmailAuditLog | null = null;

  ngOnInit() {
    this.checkConnection(true);
  }

  // ===================== HEALTH CHECK =====================
  checkConnection(reloadOnSuccess = false) {
    this.backendChecking = true;
    this.api.checkHealth().subscribe(healthy => {
      this.isBackendConnected = healthy;
      this.backendChecking = false;
      if (healthy) {
        this.loadAvailableModels();
        if (reloadOnSuccess) {
          this.loadPatients();
          this.loadConsultations();
          this.loadEmailLogs();
        }
      } else {
        this.showAlert(`Cannot reach Backend API at ${this.api.baseUrl}. Make sure 'dotnet run' is running.`, 'warning');
      }
    });
  }

  showAlert(message: string, type: 'success' | 'danger' | 'warning' | 'info' = 'info') {
    this.alertMessage = message;
    this.alertType = type;
    setTimeout(() => {
      if (this.alertMessage === message) {
        this.alertMessage = null;
      }
    }, 6000);
  }

  // ===================== PATIENT METHODS =====================
  loadPatients() {
    this.loadingPatients = true;
    this.api.getPatients({
      searchTerm: this.searchTerm || undefined,
      gender: this.selectedGender || undefined,
      pageNumber: this.currentPage,
      pageSize: this.pageSize
    }).subscribe({
      next: (res) => {
        this.patientsPaged = res.data;
        this.loadingPatients = false;
      },
      error: (err) => {
        this.loadingPatients = false;
        this.showAlert('Failed to load patients. Check API connection.', 'danger');
      }
    });
  }

  onSearch() {
    this.currentPage = 1;
    this.loadPatients();
  }

  changePage(page: number) {
    if (page < 1 || (this.patientsPaged && page > this.patientsPaged.totalPages)) return;
    this.currentPage = page;
    this.loadPatients();
  }

  openCreatePatientModal() {
    this.isEditingPatient = false;
    this.editingPatientId = null;
    this.patientForm = {
      firstName: '',
      lastName: '',
      email: '',
      phoneNumber: '',
      age: 30,
      gender: 'Female',
      dateOfBirth: '',
      address: '',
      medicalNotes: ''
    };
    this.selectedPictureFile = null;
    this.picturePreviewUrl = null;
    this.showPatientModal = true;
  }

  openEditPatientModal(patient: PatientSummary | PatientDetail) {
    this.isEditingPatient = true;
    this.editingPatientId = patient.id;
    this.patientForm = {
      firstName: patient.firstName,
      lastName: patient.lastName,
      email: patient.email,
      phoneNumber: patient.phoneNumber || '',
      age: patient.age,
      gender: patient.gender,
      dateOfBirth: '',
      address: '',
      medicalNotes: ''
    };

    if ('medicalNotes' in patient) {
      this.patientForm.medicalNotes = (patient as PatientDetail).medicalNotes || '';
      this.patientForm.address = (patient as PatientDetail).address || '';
      if ((patient as PatientDetail).dateOfBirth) {
        this.patientForm.dateOfBirth = (patient as PatientDetail).dateOfBirth!.substring(0, 10);
      }
    }

    this.selectedPictureFile = null;
    this.picturePreviewUrl = patient.pictureUrl || null;
    this.showPatientModal = true;
  }

  onPictureSelected(event: any) {
    const file = event.target.files?.[0];
    if (file) {
      this.selectedPictureFile = file;
      const reader = new FileReader();
      reader.onload = () => {
        this.picturePreviewUrl = reader.result as string;
      };
      reader.readAsDataURL(file);
    }
  }

  savePatient() {
    if (!this.patientForm.firstName?.trim() || !this.patientForm.lastName?.trim() || !this.patientForm.email?.trim()) {
      this.showAlert('Please fill in required fields (First Name, Last Name, and Email)', 'warning');
      return;
    }

    this.savingPatient = true;
    const formData = new FormData();
    formData.append('FirstName', this.patientForm.firstName.trim());
    formData.append('LastName', this.patientForm.lastName.trim());
    formData.append('Email', this.patientForm.email.trim());
    formData.append('Age', (this.patientForm.age ?? 0).toString());
    formData.append('Gender', this.patientForm.gender);

    if (this.patientForm.phoneNumber?.trim()) formData.append('PhoneNumber', this.patientForm.phoneNumber.trim());
    if (this.patientForm.dateOfBirth?.trim()) formData.append('DateOfBirth', this.patientForm.dateOfBirth.trim());
    if (this.patientForm.address?.trim()) formData.append('Address', this.patientForm.address.trim());
    if (this.patientForm.medicalNotes?.trim()) formData.append('MedicalNotes', this.patientForm.medicalNotes.trim());
    if (this.selectedPictureFile) formData.append('Picture', this.selectedPictureFile);

    if (this.isEditingPatient && this.editingPatientId) {
      this.api.updatePatient(this.editingPatientId, formData).subscribe({
        next: (res) => {
          this.savingPatient = false;
          this.showPatientModal = false;
          this.showAlert(`Patient #${this.editingPatientId} updated successfully.`, 'success');
          this.loadPatients();
          if (this.selectedPatient?.id === this.editingPatientId) {
            this.viewPatientDetail(this.editingPatientId);
          }
        },
        error: (err) => {
          this.savingPatient = false;
          this.showAlert(err.error?.message || 'Error updating patient', 'danger');
        }
      });
    } else {
      this.api.createPatient(formData).subscribe({
        next: (res) => {
          this.savingPatient = false;
          this.showPatientModal = false;
          this.showAlert(`Patient created! Azure picture uploaded & dummy welcome email sent to ${res.data.email}.`, 'success');
          this.loadPatients();
          this.loadEmailLogs();
        },
        error: (err) => {
          this.savingPatient = false;
          this.showAlert(err.error?.message || 'Error creating patient', 'danger');
        }
      });
    }
  }

  deletePatient(id: number, name: string) {
    if (!confirm(`Are you sure you want to delete patient "${name}"? This will cascade-delete their consultations and remove their picture from Azure Blob Storage.`)) {
      return;
    }

    this.api.deletePatient(id).subscribe({
      next: () => {
        this.showAlert(`Patient "${name}" deleted successfully.`, 'success');
        this.loadPatients();
        if (this.selectedPatient?.id === id) {
          this.selectedPatient = null;
        }
      },
      error: (err) => {
        this.showAlert(err.error?.message || 'Error deleting patient', 'danger');
      }
    });
  }

  viewPatientDetail(id: number) {
    this.loadingPatientDetail = true;
    this.api.getPatientById(id).subscribe({
      next: (res) => {
        this.selectedPatient = res.data;
        this.loadingPatientDetail = false;
      },
      error: (err) => {
        this.loadingPatientDetail = false;
        this.showAlert('Could not load patient details', 'danger');
      }
    });
  }

  // ===================== CONSULTATION METHODS =====================
  loadConsultations() {
    this.loadingConsultations = true;
    this.api.getConsultations(this.filterConsultationPatientId || undefined).subscribe({
      next: (res) => {
        this.consultations = res.data;
        this.loadingConsultations = false;
      },
      error: () => {
        this.loadingConsultations = false;
      }
    });
  }

  openCreateConsultationModal(patientId?: number) {
    this.isEditingConsultation = false;
    this.editingConsultationId = null;
    this.consultationForm = {
      patientId: patientId || this.selectedPatient?.id || 1,
      doctorName: 'Dr. Sarah Jenkins, MD',
      consultationDate: new Date().toISOString().substring(0, 10),
      diagnosis: '',
      symptoms: '',
      notes: '',
      treatmentPlan: '',
      prescription: '',
      status: 'Scheduled',
      fee: 150.00
    };
    this.showConsultationModal = true;
  }

  openEditConsultationModal(c: Consultation) {
    this.isEditingConsultation = true;
    this.editingConsultationId = c.id;
    this.consultationForm = {
      patientId: c.patientId,
      doctorName: c.doctorName,
      consultationDate: c.consultationDate.substring(0, 10),
      diagnosis: c.diagnosis,
      symptoms: c.symptoms || '',
      notes: c.notes || '',
      treatmentPlan: c.treatmentPlan || '',
      prescription: c.prescription || '',
      status: c.status,
      fee: c.fee
    };
    this.showConsultationModal = true;
  }

  saveConsultation() {
    if (!this.consultationForm.diagnosis || !this.consultationForm.doctorName) {
      this.showAlert('Please provide Attending Doctor and Diagnosis', 'warning');
      return;
    }

    this.savingConsultation = true;
    if (this.isEditingConsultation && this.editingConsultationId) {
      this.api.updateConsultation(this.editingConsultationId, {
        doctorName: this.consultationForm.doctorName,
        consultationDate: this.consultationForm.consultationDate,
        diagnosis: this.consultationForm.diagnosis,
        symptoms: this.consultationForm.symptoms,
        notes: this.consultationForm.notes,
        treatmentPlan: this.consultationForm.treatmentPlan,
        prescription: this.consultationForm.prescription,
        status: this.consultationForm.status || 'Scheduled',
        fee: this.consultationForm.fee || 0
      }).subscribe({
        next: () => {
          this.savingConsultation = false;
          this.showConsultationModal = false;
          this.showAlert(`Consultation #${this.editingConsultationId} updated successfully.`, 'success');
          this.loadConsultations();
          if (this.selectedPatient) this.viewPatientDetail(this.selectedPatient.id);
        },
        error: (err) => {
          this.savingConsultation = false;
          this.showAlert(err.error?.message || 'Error updating consultation', 'danger');
        }
      });
    } else {
      this.api.createConsultation(this.consultationForm).subscribe({
        next: () => {
          this.savingConsultation = false;
          this.showConsultationModal = false;
          this.showAlert('Consultation scheduled successfully.', 'success');
          this.loadConsultations();
          if (this.selectedPatient) this.viewPatientDetail(this.selectedPatient.id);
        },
        error: (err) => {
          this.savingConsultation = false;
          this.showAlert(err.error?.message || 'Error scheduling consultation', 'danger');
        }
      });
    }
  }

  deleteConsultation(id: number) {
    if (!confirm(`Are you sure you want to delete consultation #${id}?`)) return;

    this.api.deleteConsultation(id).subscribe({
      next: () => {
        this.showAlert(`Consultation #${id} removed.`, 'success');
        this.loadConsultations();
        if (this.selectedPatient) this.viewPatientDetail(this.selectedPatient.id);
      },
      error: (err) => {
        this.showAlert(err.error?.message || 'Error deleting consultation', 'danger');
      }
    });
  }

  // ===================== AI METHODS =====================
  loadAvailableModels() {
    this.loadingModels = true;
    this.api.getAiModels(this.aiApiKeyOverride || undefined).subscribe({
      next: (res) => {
        this.availableModels = res.data || [];
        this.loadingModels = false;
      },
      error: () => {
        this.loadingModels = false;
      }
    });
  }

  quickSummarizePatient(patientId: number) {
    this.summaryPatientId = patientId;
    this.activeTab = 'ai';
    this.aiSubTab = 'summary';
    this.runLiveSummary();
  }

  runLiveSummary() {
    if (!this.summaryPatientId) return;

    this.generatingSummary = true;
    this.liveSummaryResult = null;

    this.api.getLivePatientSummary(
      this.summaryPatientId,
      this.summaryCustomPrompt,
      this.aiApiKeyOverride || undefined,
      this.aiModelOverride || undefined
    ).subscribe({
      next: (res) => {
        this.liveSummaryResult = res.data;
        this.generatingSummary = false;
      },
      error: (err) => {
        this.generatingSummary = false;
        this.showAlert(err.error?.message || 'AI generation failed. Check API key or network.', 'danger');
      }
    });
  }

  sendChatMessage() {
    if (!this.chatMessage.trim()) return;

    const userText = this.chatMessage.trim();
    this.chatHistory.push({ role: 'user', content: userText });
    this.chatMessage = '';
    this.sendingChat = true;

    this.api.chatWithAi(
      userText,
      this.chatHistory,
      this.chatContextPatientId || undefined,
      this.aiApiKeyOverride || undefined,
      this.aiModelOverride || undefined
    ).subscribe({
      next: (res) => {
        this.sendingChat = false;
        this.chatHistory.push({ role: 'model', content: res.data.reply });
      },
      error: (err) => {
        this.sendingChat = false;
        this.chatHistory.push({ role: 'model', content: `[Error: ${err.error?.message || 'Failed to reach AI Agent'}]` });
      }
    });
  }

  runTriage() {
    if (!this.triageForm.symptoms.trim()) return;

    this.runningTriage = true;
    this.triageResult = null;

    this.api.triageSymptoms(
      this.triageForm.symptoms,
      this.triageForm.patientAge,
      this.triageForm.gender,
      this.triageForm.conditions,
      this.aiApiKeyOverride || undefined,
      this.aiModelOverride || undefined
    ).subscribe({
      next: (res) => {
        this.triageResult = res.data;
        this.runningTriage = false;
      },
      error: (err) => {
        this.runningTriage = false;
        this.showAlert(err.error?.message || 'Triage failed.', 'danger');
      }
    });
  }

  // ===================== EMAIL METHODS =====================
  loadEmailLogs() {
    this.loadingEmails = true;
    this.api.getEmailLogs().subscribe({
      next: (res) => {
        this.emailLogs = res.data;
        this.loadingEmails = false;
      },
      error: () => {
        this.loadingEmails = false;
      }
    });
  }

  openEmailPreview(email: EmailAuditLog) {
    this.selectedEmailBody = email;
  }
}
