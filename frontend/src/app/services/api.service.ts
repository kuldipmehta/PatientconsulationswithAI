import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AuthService } from '../core/auth.service';
import {
  AiChatResponse,
  AiInteractionLog,
  AiSourceCitation,
  AiStatusInfo,
  AiTriageResponse,
  ApiResponse,
  AuditLog,
  Consultation,
  CreateConsultationDto,
  Doctor,
  DoctorAvailability,
  EmailAuditLog,
  LivePatientSummary,
  PagedResult,
  PatientDetail,
  PatientDocumentDto,
  PatientFilterParams,
  PatientSummary,
  PendingActionProposal,
  RagSearchResultDto,
  UpdateConsultationDto,
  UploadPatientDocumentDto
} from '../models/patient.models';

export interface SseChatEvent {
  type: 'meta' | 'token' | 'done';
  data: {
    sessionId?: string;
    isSimulatedMode?: boolean;
    simulatedModeNotice?: string | null;
    emergencyRuleTriggered?: boolean;
    toolsExecuted?: string[];
    pendingActions?: PendingActionProposal[];
    sourceCitations?: AiSourceCitation[];
    text?: string;
    disclaimer?: string;
  };
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly baseUrl = '/api';

  // ==================== HEALTH & DOCTORS ====================
  getHealth(): Observable<Record<string, unknown>> {
    return this.http.get<Record<string, unknown>>('/health');
  }

  getDoctors(): Observable<ApiResponse<Doctor[]>> {
    return this.http.get<ApiResponse<Doctor[]>>(`${this.baseUrl}/doctors`);
  }

  getDoctorAvailability(doctorId: number, dateUtc: string): Observable<ApiResponse<DoctorAvailability>> {
    const params = new HttpParams().set('dateUtc', dateUtc);
    return this.http.get<ApiResponse<DoctorAvailability>>(`${this.baseUrl}/doctors/${doctorId}/availability`, { params });
  }

  // ==================== PATIENTS ====================
  getPatients(filter: PatientFilterParams = {}): Observable<ApiResponse<PagedResult<PatientSummary>>> {
    let params = new HttpParams();
    if (filter.searchTerm) params = params.set('searchTerm', filter.searchTerm);
    if (filter.gender) params = params.set('gender', filter.gender);
    if (filter.pageNumber) params = params.set('pageNumber', filter.pageNumber);
    if (filter.pageSize) params = params.set('pageSize', filter.pageSize);
    if (filter.sortBy) params = params.set('sortBy', filter.sortBy);
    if (filter.sortDescending !== undefined) params = params.set('sortDescending', filter.sortDescending);

    return this.http.get<ApiResponse<PagedResult<PatientSummary>>>(`${this.baseUrl}/patients`, { params });
  }

  getPatientById(id: number): Observable<ApiResponse<PatientDetail>> {
    return this.http.get<ApiResponse<PatientDetail>>(`${this.baseUrl}/patients/${id}`);
  }

  fetchProtectedPictureBlob(url: string): Observable<Blob> {
    return this.http.get(url, { responseType: 'blob' });
  }

  createPatient(formData: FormData): Observable<ApiResponse<PatientDetail>> {
    return this.http.post<ApiResponse<PatientDetail>>(`${this.baseUrl}/patients`, formData);
  }

  updatePatient(id: number, formData: FormData): Observable<ApiResponse<PatientDetail>> {
    return this.http.put<ApiResponse<PatientDetail>>(`${this.baseUrl}/patients/${id}`, formData);
  }

  deletePatient(id: number): Observable<ApiResponse<object>> {
    return this.http.delete<ApiResponse<object>>(`${this.baseUrl}/patients/${id}`);
  }

  // ==================== CONSULTATIONS ====================
  getConsultations(patientId?: number): Observable<ApiResponse<Consultation[]>> {
    let params = new HttpParams();
    if (patientId) params = params.set('patientId', patientId);
    return this.http.get<ApiResponse<Consultation[]>>(`${this.baseUrl}/consultations`, { params });
  }

  createConsultation(dto: CreateConsultationDto): Observable<ApiResponse<Consultation>> {
    return this.http.post<ApiResponse<Consultation>>(`${this.baseUrl}/consultations`, dto);
  }

  updateConsultation(id: number, dto: UpdateConsultationDto): Observable<ApiResponse<Consultation>> {
    return this.http.put<ApiResponse<Consultation>>(`${this.baseUrl}/consultations/${id}`, dto);
  }

  deleteConsultation(id: number): Observable<ApiResponse<object>> {
    return this.http.delete<ApiResponse<object>>(`${this.baseUrl}/consultations/${id}`);
  }

  // ==================== EMAIL & AUDIT LOGS ====================
  getEmailLogs(): Observable<ApiResponse<EmailAuditLog[]>> {
    return this.http.get<ApiResponse<EmailAuditLog[]>>(`${this.baseUrl}/emails/logs`);
  }

  getAuditLogs(take = 100): Observable<ApiResponse<AuditLog[]>> {
    return this.http.get<ApiResponse<AuditLog[]>>(`${this.baseUrl}/audit`, {
      params: new HttpParams().set('take', take)
    });
  }

  getAiInteractionLogs(take = 100): Observable<ApiResponse<AiInteractionLog[]>> {
    return this.http.get<ApiResponse<AiInteractionLog[]>>(`${this.baseUrl}/audit/ai`, {
      params: new HttpParams().set('take', take)
    });
  }

  // ==================== CLINICAL AI AGENT ====================
  getAiStatus(): Observable<ApiResponse<AiStatusInfo>> {
    return this.http.get<ApiResponse<AiStatusInfo>>(`${this.baseUrl}/ai/status`);
  }

  getAiModels(): Observable<ApiResponse<string[]>> {
    return this.http.get<ApiResponse<string[]>>(`${this.baseUrl}/ai/models`);
  }

  getLivePatientSummary(patientId: number, customInstructions?: string, model?: string): Observable<ApiResponse<LivePatientSummary>> {
    return this.http.post<ApiResponse<LivePatientSummary>>(
      `${this.baseUrl}/ai/patients/${patientId}/live-summary`,
      {
        customInstructions: customInstructions || undefined,
        model: model || undefined
      }
    );
  }

  chatWithAgent(message: string, sessionId?: string, patientContextId?: number | null, model?: string): Observable<ApiResponse<AiChatResponse>> {
    return this.http.post<ApiResponse<AiChatResponse>>(`${this.baseUrl}/ai/chat`, {
      message,
      sessionId: sessionId || undefined,
      patientContextId: patientContextId || undefined,
      model: model || undefined
    });
  }

  streamChatWithAgent(
    message: string,
    sessionId?: string,
    patientContextId?: number | null,
    model?: string
  ): Observable<SseChatEvent> {
    return new Observable<SseChatEvent>(subscriber => {
      const controller = new AbortController();
      const token = this.auth.token();

      fetch(`${this.baseUrl}/ai/chat/stream`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${token}` } : {})
        },
        body: JSON.stringify({
          message,
          sessionId: sessionId || undefined,
          patientContextId: patientContextId || undefined,
          model: model || undefined
        }),
        signal: controller.signal
      })
        .then(async response => {
          if (!response.ok || !response.body) {
            let errorMessage = `Streaming failed with status ${response.status}`;
            try {
              const text = await response.text();
              if (text) {
                const parsed = JSON.parse(text) as { message?: string; errors?: string[] };
                if (parsed?.errors?.length) {
                  errorMessage = `${parsed.message || errorMessage}: ${parsed.errors.join(', ')}`;
                } else if (parsed?.message) {
                  errorMessage = parsed.message;
                }
              }
            } catch {
              // Fall back to status-based error message if response body is not JSON.
            }
            throw new Error(errorMessage);
          }

          const reader = response.body.getReader();
          const decoder = new TextDecoder();
          let buffer = '';

          while (true) {
            const { value, done } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });
            const blocks = buffer.split('\n\n');
            buffer = blocks.pop() ?? '';

            for (const block of blocks) {
              const event = this.parseSseBlock(block);
              if (event) {
                subscriber.next(event);
              }
            }
          }

          if (buffer.trim()) {
            const trailing = this.parseSseBlock(buffer);
            if (trailing) subscriber.next(trailing);
          }
          subscriber.complete();
        })
        .catch(err => {
          if (!controller.signal.aborted) {
            subscriber.error(err);
          }
        });

      return () => controller.abort();
    });
  }

  confirmPendingAiAction(actionId: string): Observable<ApiResponse<Consultation>> {
    return this.http.post<ApiResponse<Consultation>>(`${this.baseUrl}/ai/actions/${encodeURIComponent(actionId)}/confirm`, {});
  }

  cancelPendingAiAction(actionId: string): Observable<ApiResponse<boolean>> {
    return this.http.post<ApiResponse<boolean>>(`${this.baseUrl}/ai/actions/${encodeURIComponent(actionId)}/cancel`, {});
  }

  triageSymptoms(
    symptoms: string,
    patientAge?: number | null,
    gender?: string,
    existingConditions?: string,
    model?: string
  ): Observable<ApiResponse<AiTriageResponse>> {
    const params = model ? new HttpParams().set('model', model) : undefined;
    return this.http.post<ApiResponse<AiTriageResponse>>(
      `${this.baseUrl}/ai/triage`,
      {
        symptoms,
        patientAge: patientAge || undefined,
        gender: gender || undefined,
        existingConditions: existingConditions || undefined
      },
      { params }
    );
  }

  // ==================== PATIENT DOCUMENTS & RAG ====================
  getPatientDocuments(patientId: number): Observable<ApiResponse<PatientDocumentDto[]>> {
    return this.http.get<ApiResponse<PatientDocumentDto[]>>(`${this.baseUrl}/patients/${patientId}/documents`);
  }

  uploadPatientDocument(patientId: number, file: File, dto: UploadPatientDocumentDto): Observable<ApiResponse<PatientDocumentDto>> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    formData.append('documentType', dto.documentType || 'General');
    if (dto.consultationId) {
      formData.append('consultationId', dto.consultationId.toString());
    }
    if (dto.summary) {
      formData.append('summary', dto.summary);
    }

    return this.http.post<ApiResponse<PatientDocumentDto>>(`${this.baseUrl}/patients/${patientId}/documents`, formData);
  }

  deletePatientDocument(patientId: number, documentId: number): Observable<ApiResponse<boolean>> {
    return this.http.delete<ApiResponse<boolean>>(`${this.baseUrl}/patients/${patientId}/documents/${documentId}`);
  }

  searchPatientRag(patientId: number, query: string, topK = 5, minScore = 0.35): Observable<ApiResponse<RagSearchResultDto[]>> {
    return this.http.post<ApiResponse<RagSearchResultDto[]>>(`${this.baseUrl}/patients/${patientId}/rag/search`, {
      query,
      topK,
      minScore
    });
  }

  reindexPatientConsultations(patientId: number): Observable<ApiResponse<{ patientId: number; consultationsIndexed: number }>> {
    return this.http.post<ApiResponse<{ patientId: number; consultationsIndexed: number }>>(`${this.baseUrl}/patients/${patientId}/rag/reindex`, {});
  }

  private parseSseBlock(block: string): SseChatEvent | null {
    const lines = block.split('\n').map(l => l.trim()).filter(Boolean);
    let eventType: 'meta' | 'token' | 'done' = 'token';
    let dataLine = '';

    for (const line of lines) {
      if (line.startsWith('event:')) {
        const typeStr = line.slice(6).trim();
        if (typeStr === 'meta' || typeStr === 'token' || typeStr === 'done') {
          eventType = typeStr;
        }
      } else if (line.startsWith('data:')) {
        dataLine += line.slice(5).trim();
      }
    }

    if (!dataLine) return null;
    try {
      return { type: eventType, data: JSON.parse(dataLine) };
    } catch {
      return null;
    }
  }
}
