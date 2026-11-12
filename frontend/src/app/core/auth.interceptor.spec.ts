import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.setItem('caresync_jwt_token', 'test-jwt-token');
    sessionStorage.setItem('caresync_user_profile', JSON.stringify({
      id: 1,
      email: 'admin@patientportal.com',
      fullName: 'System Admin',
      role: 'Admin',
      roleName: 'Admin'
    }));

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting()
      ]
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  it('attaches Bearer Authorization header to /api requests', () => {
    http.get('/api/patients').subscribe();

    const req = httpMock.expectOne('/api/patients');
    expect(req.request.headers.get('Authorization')).toBe('Bearer test-jwt-token');
    expect(req.request.headers.has('X-Gemini-Api-Key')).toBeFalse();
    req.flush({ success: true, data: [] });
  });
});
