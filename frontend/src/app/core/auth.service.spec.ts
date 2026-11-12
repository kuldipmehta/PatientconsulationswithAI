import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([])
      ]
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate');
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  it('stores JWT session and exposes role permissions after login', () => {
    service.login({ email: 'sarah.jenkins@patientportal.com', password: 'Doctor@12345' }).subscribe();

    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    req.flush({
      success: true,
      message: 'Authenticated',
      data: {
        accessToken: 'jwt-abc-123',
        expiresAtUtc: '2026-12-01T00:00:00Z',
        user: {
          id: 2,
          email: 'sarah.jenkins@patientportal.com',
          fullName: 'Dr. Sarah Jenkins, MD',
          role: 'Doctor',
          roleName: 'Doctor',
          doctorId: 1
        }
      }
    });

    expect(service.isAuthenticated()).toBeTrue();
    expect(service.isDoctor()).toBeTrue();
    expect(service.isAdmin()).toBeFalse();
    expect(service.canUseAi()).toBeTrue();
    expect(service.canViewEmails()).toBeFalse();
  });

  it('clears session on logout and navigates to login', () => {
    sessionStorage.setItem('caresync_jwt_token', 'tok');
    service.logout(true);
    expect(service.isAuthenticated()).toBeFalse();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });
});
