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

export type UserRole = 'Admin' | 'Doctor' | 'Receptionist';

export interface UserProfile {
  id: number;
  email: string;
  fullName: string;
  role: UserRole;
  roleName: string;
  doctorId?: number | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  user: UserProfile;
}

export interface SetPasswordRequest {
  token: string;
  newPassword: string;
}

export interface Doctor {
  id: number;
  fullName: string;
  specialty: string;
  email: string;
  startHourUtc: number;
  endHourUtc: number;
  slotDurationMinutes: number;
}

export interface TimeSlot {
  startUtc: string;
  endUtc: string;
  isAvailable: boolean;
}

export interface DoctorAvailability {
  doctorId: number;
  doctorName: string;
  specialty: string;
  dateUtc: string;
  slots: TimeSlot[];
}

export type ConsultationStatus = 'Scheduled' | 'Completed' | 'Cancelled' | 'FollowUpRequired';

export interface Consultation {
  id: number;
  patientId: number;
  patientName: string;
  doctorId?: number | null;
  doctorName: string;
  consultationDate: string;
  durationMinutes: number;
  diagnosis: string;
  symptoms?: string;
  notes?: string;
  treatmentPlan?: string;
  prescription?: string;
  status: ConsultationStatus;
  statusName?: string;
  fee?: number | null;
  rowVersion: string;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface CreateConsultationDto {
  patientId: number;
  doctorId?: number | null;
  doctorName: string;
  consultationDate: string;
  durationMinutes?: number;
  diagnosis: string;
  symptoms?: string;
  notes?: string;
  treatmentPlan?: string;
  prescription?: string;
  status?: ConsultationStatus;
  fee?: number | null;
}

export interface UpdateConsultationDto {
  doctorId?: number | null;
  doctorName: string;
  consultationDate: string;
  durationMinutes?: number;
  diagnosis: string;
  symptoms?: string;
  notes?: string;
  treatmentPlan?: string;
  prescription?: string;
  status: ConsultationStatus;
  fee?: number | null;
  rowVersion?: string;
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
  dateOfBirth?: string;
  profilePictureUrl?: string;
  primaryDoctorId?: number | null;
  primaryDoctorName?: string | null;
  rowVersion: string;
  consultationCount: number;
  createdAtUtc: string;
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
  profilePictureUrl?: string;
  medicalNotes?: string;
  primaryDoctorId?: number | null;
  primaryDoctorName?: string | null;
  rowVersion: string;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
  consultationCount: number;
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
  patientId: number;
  recipientEmail: string;
  recipientName: string;
  subject: string;
  body: string;
  status: string;
  sentAtUtc: string;
}

export interface AuditLog {
  id: number;
  userId?: number | null;
  userEmail: string;
  userRole: string;
  action: string;
  entityName: string;
  entityId: string;
  changesJson?: string | null;
  timestampUtc: string;
}

export interface AiInteractionLog {
  id: number;
  userId?: number | null;
  userEmail: string;
  userRole: string;
  endpoint: string;
  patientId?: number | null;
  sanitizedPrompt: string;
  toolCallsJson?: string | null;
  responseSummary: string;
  modelUsed: string;
  isSimulated: boolean;
  durationMs: number;
  createdAtUtc: string;
}

export interface PendingActionProposal {
  actionId: string;
  actionType: string;
  confirmationPrompt: string;
  patientId: number;
  doctorId?: number | null;
  doctorName: string;
  proposedDateUtc: string;
  durationMinutes: number;
  diagnosisOrReason: string;
  status: string;
  expiresAtUtc: string;
}

export interface AiSourceCitation {
  consultationId: number;
  consultationDate: string;
  doctorName: string;
  clinicalSnippet: string;
}

export interface AiStatusInfo {
  agentName: string;
  engine: string;
  model: string;
  liveGeminiApiConnected: boolean;
  isSimulatedMode: boolean;
  simulatedModeNotice?: string | null;
  disclaimer: string;
  availableTools: string[];
  timestamp: string;
}

export interface LivePatientSummary {
  patientId: number;
  patientLabel: string;
  totalConsultationsFetchedFromDb: number;
  aiGeneratedSummary: string;
  aiIdentifiedRisks: string[];
  aiRecommendedActions: string[];
  urgencyAssessment: string;
  sourceCitations: AiSourceCitation[];
  sanitizedClinicalDataUsed?: Record<string, unknown> | null;
  isSimulatedMode: boolean;
  simulatedModeNotice?: string | null;
  disclaimer: string;
  modelUsed: string;
  generatedAtUtc: string;
}

export interface AiChatMessage {
  role: 'user' | 'model';
  content: string;
  toolsExecuted?: string[];
  pendingActions?: PendingActionProposal[];
  sourceCitations?: AiSourceCitation[];
  isSimulatedMode?: boolean;
  simulatedModeNotice?: string | null;
  emergencyRuleTriggered?: boolean;
  disclaimer?: string;
}

export interface AiChatResponse {
  sessionId: string;
  reply: string;
  toolsExecuted: string[];
  pendingActions: PendingActionProposal[];
  sourceCitations: AiSourceCitation[];
  success: boolean;
  isSimulatedMode: boolean;
  simulatedModeNotice?: string | null;
  emergencyRuleTriggered: boolean;
  disclaimer: string;
  modelUsed?: string;
  timestamp: string;
}

export interface AiTriageResponse {
  urgencyLevel: string;
  recommendedSpecialty: string;
  triageNotes: string;
  redFlagQuestions: string[];
  suggestedNextSteps: string[];
  ruleBasedEmergencyTriggered: boolean;
  matchedEmergencyRule?: string | null;
  isSimulatedMode: boolean;
  simulatedModeNotice?: string | null;
  disclaimer: string;
  analyzedAtUtc: string;
}
