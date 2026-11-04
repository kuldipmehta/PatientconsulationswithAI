import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpHeaders } from '@angular/common/http';
import { Observable, catchError, map, of } from 'rxjs';
import {
  ApiResponse,
  PagedResult,
  PatientSummary,
  PatientDetail,
  PatientFilterParams,
  Consultation,
  CreateConsultationDto,
  UpdateConsultationDto,
  EmailAuditLog,
  LivePatientSummary,
  AiChatMessage,
  AiChatResponse,
  AiTriageResponse
} from '../models/patient.models';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private http = inject(HttpClient);
  
  // API URL - defaults to ASP.NET Core local server port 5056 or persisted URL
  public baseUrl = localStorage.getItem('pm_api_url') || 'http://localhost:5056';

  setBaseUrl(url: string) {
    this.baseUrl = url.replace(/\/$/, '');
    localStorage.setItem('pm_api_url', this.baseUrl);
  }

  // Health Check with automatic fallback probe between port 5056 and 7102
  checkHealth(): Observable<boolean> {
    return this.http.get<{ status: string }>(`${this.baseUrl}/health`).pipe(
      map(res => res.status === 'Healthy'),
      catchError(() => {
        // If current port failed, try alternate port automatically
        const alternateUrl = this.baseUrl.includes('5056') 
          ? 'https://localhost:7102' 
          : 'http://localhost:5056';

        return this.http.get<{ status: string }>(`${alternateUrl}/health`).pipe(
          map(res => {
            if (res.status === 'Healthy') {
              this.setBaseUrl(alternateUrl);
              return true;
            }
            return false;
          }),
          catchError(() => of(false))
        );
      })
    );
  }

  // ===================== PATIENTS =====================
  getPatients(params?: PatientFilterParams): Observable<ApiResponse<PagedResult<PatientSummary>>> {
    let httpParams = new HttpParams();
    if (params?.searchTerm) httpParams = httpParams.set('searchTerm', params.searchTerm);
    if (params?.gender) httpParams = httpParams.set('gender', params.gender);
    if (params?.pageNumber) httpParams = httpParams.set('pageNumber', params.pageNumber.toString());
    if (params?.pageSize) httpParams = httpParams.set('pageSize', params.pageSize.toString());
    if (params?.sortBy) httpParams = httpParams.set('sortBy', params.sortBy);
    if (params?.sortDescending !== undefined) httpParams = httpParams.set('sortDescending', params.sortDescending.toString());

    return this.http.get<ApiResponse<PagedResult<PatientSummary>>>(`${this.baseUrl}/api/patients`, { params: httpParams });
  }

  getPatientById(id: number): Observable<ApiResponse<PatientDetail>> {
    return this.http.get<ApiResponse<PatientDetail>>(`${this.baseUrl}/api/patients/${id}`);
  }

  createPatient(formData: FormData): Observable<ApiResponse<PatientDetail>> {
    return this.http.post<ApiResponse<PatientDetail>>(`${this.baseUrl}/api/patients`, formData);
  }

  updatePatient(id: number, formData: FormData): Observable<ApiResponse<PatientDetail>> {
    return this.http.put<ApiResponse<PatientDetail>>(`${this.baseUrl}/api/patients/${id}`, formData);
  }

  deletePatient(id: number): Observable<ApiResponse<boolean>> {
    return this.http.delete<ApiResponse<boolean>>(`${this.baseUrl}/api/patients/${id}`);
  }

  // ===================== CONSULTATIONS =====================
  getConsultations(patientId?: number): Observable<ApiResponse<Consultation[]>> {
    let params = new HttpParams();
    if (patientId) params = params.set('patientId', patientId.toString());
    return this.http.get<ApiResponse<Consultation[]>>(`${this.baseUrl}/api/consultations`, { params });
  }

  getConsultationById(id: number): Observable<ApiResponse<Consultation>> {
    return this.http.get<ApiResponse<Consultation>>(`${this.baseUrl}/api/consultations/${id}`);
  }

  createConsultation(dto: CreateConsultationDto): Observable<ApiResponse<Consultation>> {
    return this.http.post<ApiResponse<Consultation>>(`${this.baseUrl}/api/consultations`, dto);
  }

  updateConsultation(id: number, dto: UpdateConsultationDto): Observable<ApiResponse<Consultation>> {
    return this.http.put<ApiResponse<Consultation>>(`${this.baseUrl}/api/consultations/${id}`, dto);
  }

  deleteConsultation(id: number): Observable<ApiResponse<boolean>> {
    return this.http.delete<ApiResponse<boolean>>(`${this.baseUrl}/api/consultations/${id}`);
  }

  // ===================== EMAIL AUDIT LOGS =====================
  getEmailLogs(): Observable<ApiResponse<EmailAuditLog[]>> {
    return this.http.get<ApiResponse<EmailAuditLog[]>>(`${this.baseUrl}/api/emails/logs`);
  }

  // ===================== AI AGENT =====================
  getAiStatus(): Observable<ApiResponse<any>> {
    return this.http.get<ApiResponse<any>>(`${this.baseUrl}/api/ai/status`);
  }

  getAiModels(apiKey?: string): Observable<ApiResponse<string[]>> {
    let headers = new HttpHeaders();
    if (apiKey) headers = headers.set('X-Gemini-ApiKey', apiKey);
    return this.http.get<ApiResponse<string[]>>(`${this.baseUrl}/api/ai/models`, { headers });
  }

  getLivePatientSummary(patientId: number, customInstructions?: string, apiKey?: string, model?: string): Observable<ApiResponse<LivePatientSummary>> {
    let headers = new HttpHeaders();
    if (apiKey) headers = headers.set('X-Gemini-ApiKey', apiKey);

    const body: any = {};
    if (customInstructions) body.customInstructions = customInstructions;
    if (apiKey) body.apiKey = apiKey;
    if (model) body.model = model;

    return this.http.post<ApiResponse<LivePatientSummary>>(`${this.baseUrl}/api/ai/patients/${patientId}/live-summary`, body, { headers });
  }

  chatWithAi(message: string, history?: AiChatMessage[], patientContextId?: number, apiKey?: string, model?: string): Observable<ApiResponse<AiChatResponse>> {
    let headers = new HttpHeaders();
    if (apiKey) headers = headers.set('X-Gemini-ApiKey', apiKey);

    const body: any = { message };
    if (history && history.length > 0) body.conversationHistory = history;
    if (patientContextId) body.patientContextId = patientContextId;
    if (apiKey) body.apiKey = apiKey;
    if (model) body.model = model;

    return this.http.post<ApiResponse<AiChatResponse>>(`${this.baseUrl}/api/ai/chat`, body, { headers });
  }

  triageSymptoms(symptoms: string, patientAge?: number, gender?: string, conditions?: string, apiKey?: string, model?: string): Observable<ApiResponse<AiTriageResponse>> {
    let headers = new HttpHeaders();
    if (apiKey) headers = headers.set('X-Gemini-ApiKey', apiKey);

    const body: any = { symptoms };
    if (patientAge !== undefined && patientAge !== null) body.patientAge = patientAge;
    if (gender) body.gender = gender;
    if (conditions) body.existingConditions = conditions;

    return this.http.post<ApiResponse<AiTriageResponse>>(`${this.baseUrl}/api/ai/triage`, body, { headers });
  }
}
