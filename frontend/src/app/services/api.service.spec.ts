import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ApiService } from './api.service';

describe('ApiService', () => {
  let api: ApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([])
      ]
    });
    api = TestBed.inject(ApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('never sends client apiKey in live summary or triage payloads', () => {
    api.getLivePatientSummary(1, 'Focus on allergies').subscribe();
    const summaryReq = httpMock.expectOne('/api/ai/patients/1/live-summary');
    expect(summaryReq.request.body.apiKey).toBeUndefined();
    expect(summaryReq.request.body.customInstructions).toBe('Focus on allergies');
    summaryReq.flush({ success: true, data: { patientId: 1 } });

    api.triageSymptoms('Moderate cough').subscribe();
    const triageReq = httpMock.expectOne('/api/ai/triage');
    expect(triageReq.request.body.apiKey).toBeUndefined();
    expect(triageReq.request.body.symptoms).toBe('Moderate cough');
    triageReq.flush({ success: true, data: { urgencyLevel: 'Moderate' } });
  });

  it('calls confirmation endpoint for human-in-the-loop pending AI actions', () => {
    api.confirmPendingAiAction('act-42').subscribe();
    const req = httpMock.expectOne('/api/ai/actions/act-42/confirm');
    expect(req.request.method).toBe('POST');
    req.flush({ success: true, data: { id: 10 } });
  });
});
