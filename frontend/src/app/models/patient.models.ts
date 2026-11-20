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
  promptVersion?: string | null;
  isSimulated: boolean;
  durationMs: number;
  latencyMs?: number;
  promptTokens?: number | null;
  completionTokens?: number | null;
  totalTokens?: number | null;
  estimatedCostUsd?: number | null;
  createdAtUtc: string;
}

export interface AiUsageDailyModelRow {
  dayUtc: string;
  modelUsed: string;
  requests: number;
  trackedRequests?: number;
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
  estimatedCostUsd: number;
  p95LatencyMs: number;
}

export interface AiUsageSummary {
  fromUtc?: string | null;
  toUtc?: string | null;
  totalRequests: number;
  trackedRequests?: number;
  totalPromptTokens: number;
  totalCompletionTokens: number;
  totalTokens: number;
  totalEstimatedCostUsd: number;
  overallP95LatencyMs: number;
  breakdown: AiUsageDailyModelRow[];
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
  sourceType?: string;
  documentId?: number | null;
  documentName?: string | null;
  chunkIndex?: number | null;
  similarityScore?: number | null;
  relevanceScore?: number | null;
  isCompressed?: boolean;
}

export interface PatientDocumentDto {
  id: number;
  patientId: number;
  consultationId?: number | null;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  documentType: string;
  status: string;
  totalChunks: number;
  summary?: string | null;
  createdAtUtc: string;
}

export interface UploadPatientDocumentDto {
  documentType: string;
  consultationId?: number | null;
  summary?: string | null;
}

export interface RagSearchResultDto {
  chunkId: number;
  patientId: number;
  documentId?: number | null;
  consultationId?: number | null;
  sourceType: string;
  sourceLabel: string;
  textContent: string;
  similarityScore: number;
  relevanceScore?: number | null;
  isCompressed?: boolean;
  createdAtUtc: string;
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

export type ClaimStatus = 'Supported' | 'Contradicted' | 'Ungrounded';

export interface ClinicalClaimVerification {
  claimText: string;
  status: ClaimStatus;
  sourceEvidence?: string | null;
  explanation?: string | null;
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
  groundingScore?: number | null;
  isGrounded?: boolean;
  wasSelfCorrected?: boolean;
  verifiedClaims?: ClinicalClaimVerification[];
  judgeResult?: LlmJudgeEvaluationResult;
  judgeLoading?: boolean;
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
  collaboratingAgents?: string[];
  pharmacovigilanceWarnings?: string[];
  injectedExemplars?: string[];
  groundingScore?: number | null;
  isGrounded?: boolean;
  wasSelfCorrected?: boolean;
  verifiedClaims?: ClinicalClaimVerification[];
  judgeResult?: LlmJudgeEvaluationResult;
  judgeLoading?: boolean;
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
  collaboratingAgents?: string[];
  pharmacovigilanceWarnings?: string[];
  injectedExemplars?: string[];
  groundingScore?: number | null;
  isGrounded?: boolean;
  wasSelfCorrected?: boolean;
  verifiedClaims?: ClinicalClaimVerification[];
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

export interface LlmJudgeMetricScore {
  metricName: string;
  score: number;
  minPassingScore: number;
  explanation: string;
  passed: boolean;
}

export interface LlmJudgeEvaluationRequest {
  query: string;
  sourceContext: string;
  generatedResponse: string;
  featureScope?: string | null;
}

export interface LlmJudgeEvaluationResult {
  overallScore: number;
  passed: boolean;
  metrics: LlmJudgeMetricScore[];
  chainOfThoughtCritique: string;
  evaluatedAtUtc: string;
}

export interface ClinicalExemplarDto {
  id: string;
  domain: string;
  scenarioTitle: string;
  query: string;
  idealOutput: string;
  clinicalRationale: string;
  relevanceScore?: number | null;
}
