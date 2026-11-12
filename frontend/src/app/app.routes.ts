import { Routes } from '@angular/router';
import { authGuard, roleGuard } from './core/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'patients' },
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'set-password',
    loadComponent: () => import('./features/auth/set-password.component').then(m => m.SetPasswordComponent)
  },
  {
    path: 'patients',
    canActivate: [authGuard],
    loadComponent: () => import('./features/patients/patients-workspace.component').then(m => m.PatientsWorkspaceComponent)
  },
  {
    path: 'consultations',
    canActivate: [authGuard],
    loadComponent: () => import('./features/consultations/consultations-workspace.component').then(m => m.ConsultationsWorkspaceComponent)
  },
  {
    path: 'ai-agent',
    canActivate: [roleGuard(['Admin', 'Doctor'])],
    loadComponent: () => import('./features/ai-agent/ai-agent-workspace.component').then(m => m.AiAgentWorkspaceComponent)
  },
  {
    path: 'emails',
    canActivate: [roleGuard(['Admin', 'Receptionist'])],
    loadComponent: () => import('./features/emails/email-logs.component').then(m => m.EmailLogsComponent)
  },
  {
    path: 'audit',
    canActivate: [roleGuard(['Admin'])],
    loadComponent: () => import('./features/audit/audit-logs.component').then(m => m.AuditLogsComponent)
  },
  { path: '**', redirectTo: 'patients' }
];
