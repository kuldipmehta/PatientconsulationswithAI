export interface ApiResponse<T> {
  success: boolean;
  message: string;
  data: T;
  errors?: string[];
}

export interface PagedResult<T> {
  items: T[];
  pageNumber: number;
  totalPages: number;
  totalCount: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export type ConsultationStatus = 'Scheduled' | 'Completed' | 'Cancelled' | 'FollowUpRequired';

export interface Consultation {
  id: number;
  patientId: number;
  patientName: string;
  doctorName: string;
  consultationDate: string;
  diagnosis: string;
  symptoms?: string;
  notes?: string;
  treatmentPlan?: string;
  prescription?: string;
  status: ConsultationStatus;
  fee: number;
  createdAt: string;
}

export interface CreateConsultationDto {
  patientId: number;
  doctorName: string;
  consultationDate: string;
  diagnosis: string;
  symptoms?: string;
  notes?: string;
  treatmentPlan?: string;
  prescription?: string;
  status?: ConsultationStatus;
  fee?: number;
}

export interface UpdateConsultationDto {
  doctorName: string;
  consultationDate: string;
  diagnosis: string;
  symptoms?: string;
  notes?: string;
  treatmentPlan?: string;
  prescription?: string;
  status: ConsultationStatus;
  fee: number;
}

export interface PatientSummary {
  id: number;
  firstName: string;
  lastName: string;
  fullName: string;
  age: number;
  gender: string;
  email: string;
  phoneNumber?: string;
  pictureUrl?: string;
  consultationCount: number;
  createdAt: string;
}

export interface PatientDetail {
  id: number;
  firstName: string;
  lastName: string;
  fullName: string;
  age: number;
  gender: string;
  email: string;
  phoneNumber?: string;
  dateOfBirth?: string;
  address?: string;
  pictureUrl?: string;
  medicalNotes?: string;
  createdAt: string;
  consultations: Consultation[];
}

export interface PatientFilterParams {
  searchTerm?: string;
  gender?: string;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
}

export interface EmailAuditLog {
  id: number;
  recipientEmail: string;
  recipientName: string;
  subject: string;
  body: string;
  isSuccess: boolean;
  sentAt: string;
}

export interface LivePatientSummary {
  patientId: number;
  patientName: string;
  totalConsultationsFetchedFromDb: number;
  aiGeneratedSummary: string;
  aiIdentifiedRisks: string[];
  aiRecommendedActions: string[];
  urgencyAssessment: string;
  liveDatabaseDataUsed: any;
  modelUsed: string;
  generatedAtUtc: string;
}

export interface AiChatMessage {
  role: 'user' | 'model';
  content: string;
}

export interface AiChatResponse {
  reply: string;
  toolsExecuted: string[];
  modelUsed: string;
  success: boolean;
}

export interface AiTriageResponse {
  urgencyLevel: string;
  recommendedSpecialty: string;
  triageNotes: string;
  redFlagQuestions: string[];
  suggestedNextSteps: string[];
  analyzedAtUtc: string;
}
