import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { extractApiErrorMessage } from '../../core/http-error.util';
import {
  AiChatMessage,
  AiStatusInfo,
  AiTriageResponse,
  LivePatientSummary,
  PatientSummary,
  PendingActionProposal,
  PythonAiEngineStatus,
  PythonAiHealth,
  PythonClinicalRecord,
  PythonJobStatus
} from '../../models/patient.models';

@Component({
  selector: 'app-ai-agent-workspace',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="ai-layout">
      <!-- Simulated Mode Warning Banner -->
      <div *ngIf="aiStatus()?.isSimulatedMode" class="banner-simulated" role="alert">
        <strong>SIMULATED MODE ACTIVE:</strong>
        {{ aiStatus()?.simulatedModeNotice || 'No live Gemini API key is configured on the server. Output is deterministic demo data and NOT real AI clinical analysis.' }}
      </div>

      <!-- Mandatory Clinical Decision Support Disclaimer -->
      <div class="banner-disclaimer" role="note">
        <strong>Clinical Decision Support Disclaimer:</strong>
        {{ aiStatus()?.disclaimer || 'AI output must be reviewed and verified by a licensed clinician before clinical or scheduling decisions.' }}
      </div>

      <section class="panel" aria-labelledby="ai-heading">
        <div class="panel-header">
          <div>
            <h2 id="ai-heading">Clinical AI Workspace</h2>
            <p class="muted">
              Server-side Gemini integration • Model: <strong>{{ aiStatus()?.model || 'gemini-flash-latest' }}</strong> •
              <span class="badge" [class.badge-warning]="aiStatus()?.isSimulatedMode" [class.badge-success]="!aiStatus()?.isSimulatedMode">
                {{ aiStatus()?.isSimulatedMode ? 'Simulated Mode' : 'Live Gemini Connected' }}
              </span>
            </p>
          </div>
          <div class="mode-tabs" role="tablist" aria-label="AI Capabilities">
            <button type="button" role="tab" class="btn btn-sm" [class.btn-primary]="mode() === 'chat'" [class.btn-secondary]="mode() !== 'chat'" (click)="mode.set('chat')">
              Streaming Chat & Booking
            </button>
            <button type="button" role="tab" class="btn btn-sm" [class.btn-primary]="mode() === 'summary'" [class.btn-secondary]="mode() !== 'summary'" (click)="mode.set('summary')">
              De-Identified Patient Summary
            </button>
            <button type="button" role="tab" class="btn btn-sm" [class.btn-primary]="mode() === 'triage'" [class.btn-secondary]="mode() !== 'triage'" (click)="mode.set('triage')">
              Symptom Triage
            </button>
            <button type="button" role="tab" class="btn btn-sm" [class.btn-primary]="mode() === 'microservice'" [class.btn-secondary]="mode() !== 'microservice'" (click)="mode.set('microservice'); checkFastApiHealth()">
              ⚡ FastAPI Microservice
            </button>
          </div>
        </div>

        <div *ngIf="errorMessage()" class="alert alert-danger" role="alert">{{ errorMessage() }}</div>
        <div *ngIf="actionFeedback()" class="alert alert-success" role="status">{{ actionFeedback() }}</div>

        <!-- TAB 1: STREAMING CHAT + HUMAN-IN-THE-LOOP ACTION CONFIRMATION -->
        <div *ngIf="mode() === 'chat'">
          <div class="context-bar">
            <div class="form-group inline-group">
              <label class="form-label" for="ai-patient-ctx">Anchor Patient Context (Optional)</label>
              <select id="ai-patient-ctx" class="form-select" [(ngModel)]="selectedPatientId">
                <option [ngValue]="null">General Clinical Directory Lookup</option>
                <option *ngFor="let p of patients()" [ngValue]="p.id">Patient #{{ p.id }} — {{ p.fullName }} ({{ p.age }}y)</option>
              </select>
            </div>
            <div class="session-pill" *ngIf="sessionId()">
              Session ID: <code>{{ sessionId() | slice:0:12 }}...</code>
            </div>
          </div>

          <div class="chat-feed" aria-live="polite">
            <div *ngIf="messages().length === 0" class="muted">
              Ask about assigned patients, check doctor availability, or request to propose a follow-up consultation (confirmation required before saving).
            </div>

            <div *ngFor="let m of messages()" class="chat-bubble" [class.user]="m.role === 'user'" [class.model]="m.role === 'model'">
              <div class="bubble-role">
                {{ m.role === 'user' ? 'Clinician' : 'Clinical AI Assistant' }}
                <span *ngIf="m.emergencyRuleTriggered" class="badge badge-danger">Emergency Rule Triggered</span>
              </div>
              <div class="bubble-text">{{ m.content }}</div>

              <div *ngIf="m.toolsExecuted?.length" class="meta-line">
                Tools: <span *ngFor="let t of m.toolsExecuted" class="badge badge-secondary">{{ t }}</span>
              </div>

              <div *ngIf="m.collaboratingAgents?.length" class="meta-line swarm-line">
                <span class="swarm-title">🤖 Specialist Swarm:</span>
                <span *ngFor="let ag of m.collaboratingAgents" class="badge badge-swarm">
                  {{ ag }}
                </span>
              </div>

              <div *ngIf="m.injectedExemplars?.length" class="meta-line exemplar-line">
                <span class="exemplar-title">📚 Dynamic Few-Shot Memory:</span>
                <span *ngFor="let ex of m.injectedExemplars" class="badge badge-exemplar" title="Dynamically retrieved from Gold-Standard Clinical Exemplar Vector Store">
                  {{ ex }}
                </span>
              </div>

              <div *ngIf="m.pharmacovigilanceWarnings?.length" class="pharmacovigilance-card">
                <div class="pv-title">⚠️ Pharmacovigilance Safety Alerts</div>
                <ul>
                  <li *ngFor="let w of m.pharmacovigilanceWarnings">{{ w }}</li>
                </ul>
              </div>

              <div *ngIf="m.sourceCitations?.length" class="meta-line">
                Grounding Sources:
                <span *ngFor="let c of m.sourceCitations" class="badge" [class.badge-primary]="c.sourceType !== 'PatientDocument'" [class.badge-secondary]="c.sourceType === 'PatientDocument'" [title]="c.clinicalSnippet || 'Clinical source citation'">
                  <ng-container *ngIf="c.sourceType === 'PatientDocument'">
                    📄 {{ c.documentName || 'Document' }} <span *ngIf="c.relevanceScore != null">(★ {{ (c.relevanceScore * 100).toFixed(0) }}%)</span><span *ngIf="c.relevanceScore == null && c.similarityScore">({{ (c.similarityScore * 100).toFixed(0) }}%)</span><span *ngIf="c.isCompressed"> ⚡</span>
                  </ng-container>
                  <ng-container *ngIf="c.sourceType !== 'PatientDocument'">
                    🩺 Visit #{{ c.consultationId }} ({{ c.consultationDate | date:'shortDate' }})<span *ngIf="c.relevanceScore != null"> (★ {{ (c.relevanceScore * 100).toFixed(0) }}%)</span><span *ngIf="c.isCompressed"> ⚡</span>
                  </ng-container>
                </span>
              </div>

              <div *ngIf="m.groundingScore != null" class="meta-line grounding-line">
                <span *ngIf="m.isGrounded && !m.wasSelfCorrected" class="badge badge-grounded" title="100% of clinical metrics, medications, and findings cross-verified against source patient records">
                  🛡️ {{ (m.groundingScore * 100).toFixed(0) }}% Grounded (Source-Verified)
                </span>
                <span *ngIf="m.wasSelfCorrected" class="badge badge-self-corrected" title="Preliminary response contained ungrounded claims that were detected and purged by the self-correction loop">
                  ⚡ Self-Corrected (Hallucination Purged)
                </span>
                <span *ngIf="!m.isGrounded && !m.wasSelfCorrected" class="badge badge-grounding-warning" title="Response contains ungrounded clinical statements">
                  ⚠️ {{ (m.groundingScore * 100).toFixed(0) }}% Grounded (Unverified Speculation)
                </span>
              </div>

              <!-- LLM-as-a-Judge Audit Action & Scorecard -->
              <div class="judge-action-row" *ngIf="m.role === 'model' && m.content">
                <button type="button" class="btn-judge-pill" (click)="runJudgeAudit(m)" [disabled]="m.judgeLoading">
                  {{ m.judgeLoading ? '⚖️ Evaluating Rubrics...' : (m.judgeResult ? '⚖️ Re-Run LLM Judge Audit' : '⚖️ Audit with LLM Judge') }}
                </button>
              </div>

              <div *ngIf="m.judgeResult as j" class="judge-scorecard" [class.judge-passed]="j.passed" [class.judge-failed]="!j.passed">
                <div class="judge-header">
                  <div class="judge-title">
                    <span class="judge-icon">{{ j.passed ? '🛡️' : '⚠️' }}</span>
                    <strong>LLM Judge Quality Gate:</strong>
                    <span class="badge" [class.badge-grounded]="j.passed" [class.badge-danger]="!j.passed">
                      {{ j.passed ? 'PASSED (Production Ready)' : 'REGRESSION BLOCKED' }}
                    </span>
                  </div>
                  <div class="judge-score">
                    Score: <strong>{{ j.overallScore.toFixed(1) }}</strong> / 5.0
                  </div>
                </div>

                <div class="judge-metrics-grid">
                  <div *ngFor="let met of j.metrics" class="judge-metric-card" [class.metric-pass]="met.passed" [class.metric-fail]="!met.passed">
                    <div class="metric-top">
                      <span class="metric-name">{{ met.metricName }}</span>
                      <span class="metric-score-tag">{{ met.score.toFixed(1) }} / 5.0</span>
                    </div>
                    <div class="metric-progress-bg">
                      <div class="metric-progress-bar" [style.width.%]="(met.score / 5) * 100" [class.bar-pass]="met.passed" [class.bar-fail]="!met.passed"></div>
                    </div>
                    <div class="metric-expl">{{ met.explanation }}</div>
                  </div>
                </div>

                <details class="judge-cot-details">
                  <summary>View Chain-of-Thought Rubric Critique</summary>
                  <pre class="judge-cot-text">{{ j.chainOfThoughtCritique }}</pre>
                </details>
              </div>

              <!-- Human-in-the-loop Confirmation Card -->
              <div *ngFor="let act of m.pendingActions" class="proposal-card">
                <div class="proposal-title">Proposed Action Requires Confirmation</div>
                <p>{{ act.confirmationPrompt }}</p>
                <div class="proposal-actions" *ngIf="act.status === 'Pending'">
                  <button type="button" class="btn btn-primary btn-sm" (click)="confirmProposal(act)">
                    Confirm & Schedule
                  </button>
                  <button type="button" class="btn btn-secondary btn-sm" (click)="cancelProposal(act)">
                    Cancel Proposal
                  </button>
                </div>
                <span *ngIf="act.status !== 'Pending'" class="badge badge-secondary">{{ act.status }}</span>
              </div>
            </div>
          </div>

          <div class="quick-prompts-row">
            <span class="quick-label">⚡ Multi-Agent Scenarios:</span>
            <button type="button" class="btn-quick-chip" (click)="setQuickPrompt('Patient is taking Warfarin. Can we prescribe Ibuprofen 400mg for acute knee swelling and book an orthopedic evaluation?')">
              💊 Warfarin + NSAID Interaction Swarm
            </button>
            <button type="button" class="btn-quick-chip" (click)="setQuickPrompt('Patient has severe penicillin allergy and acute joint pain. Can we prescribe Amoxicillin and schedule a specialist appointment?')">
              🛡️ Penicillin Allergy + Triage Swarm
            </button>
            <button type="button" class="btn-quick-chip" (click)="setQuickPrompt('What are patient Arthur Pendelton current medications, documented allergies, and most recent clinical consultation findings?')">
              🛡️ Test Grounding Verifier
            </button>
            <button type="button" class="btn-quick-chip" (click)="setQuickPrompt('Arthur Pendelton asks: Can I take Advil or Motrin 600mg for back pain? What is my history of blood pressure and medications?')">
              ⚖️ Test LLM Judge Gate (Warfarin + NSAID)
            </button>
            <button type="button" class="btn-quick-chip" (click)="setQuickPrompt('Arthur Pendelton asks: Can I take Advil or Motrin for back pain while on blood thinners? What is the clinical rationale and recommended alternative?')">
              📚 Test Vector Memory (Few-Shot Retrieval)
            </button>
          </div>

          <form class="chat-input-row" (ngSubmit)="sendChatStream()">
            <label for="chat-prompt" class="sr-only">Clinical assistant prompt</label>
            <input
              id="chat-prompt"
              name="chatInput"
              class="form-control"
              placeholder="e.g., Propose a follow-up with Dr. Sarah Jenkins for selected patient..."
              [(ngModel)]="chatInput"
              [disabled]="chatStreaming()"
            />
            <button type="submit" class="btn btn-primary" [disabled]="chatStreaming() || !chatInput.trim()">
              {{ chatStreaming() ? 'Streaming...' : 'Send' }}
            </button>
          </form>
        </div>

        <!-- TAB 2: LIVE DE-IDENTIFIED SUMMARY -->
        <div *ngIf="mode() === 'summary'">
          <div class="summary-controls">
            <div class="form-group">
              <label class="form-label" for="sum-patient">Patient Record *</label>
              <select id="sum-patient" class="form-select" [(ngModel)]="selectedPatientId">
                <option *ngFor="let p of patients()" [ngValue]="p.id">Patient #{{ p.id }} — {{ p.fullName }}</option>
              </select>
            </div>
            <div class="form-group">
              <label class="form-label" for="sum-instr">Custom Clinical Focus (Optional)</label>
              <input id="sum-instr" class="form-control" placeholder="e.g., Focus on allergies and cardiac risk" [(ngModel)]="summaryInstructions" />
            </div>
            <button type="button" class="btn btn-purple" (click)="generateSummary()" [disabled]="summaryLoading() || !selectedPatientId">
              {{ summaryLoading() ? 'Analyzing...' : 'Generate De-Identified Summary' }}
            </button>
          </div>

          <div *ngIf="liveSummary() as s" class="summary-result">
            <div class="summary-header">
              <h3>{{ s.patientLabel }}</h3>
              <div class="summary-badges-group">
                <span *ngIf="s.groundingScore != null" class="badge" [class.badge-grounded]="s.isGrounded" [class.badge-grounding-warning]="!s.isGrounded" [title]="'Automated Grounding Verifier: ' + (s.groundingScore * 100).toFixed(0) + '% fact-checked'">
                  🛡️ {{ (s.groundingScore * 100).toFixed(0) }}% Grounded
                </span>
                <span *ngIf="s.wasSelfCorrected" class="badge badge-self-corrected" title="Hallucinations detected in preliminary draft were automatically purged">
                  ⚡ Self-Corrected
                </span>
                <span class="badge badge-warning">Urgency: {{ s.urgencyAssessment }}</span>
              </div>
            </div>
            <p class="summary-body">{{ s.aiGeneratedSummary }}</p>

            <div class="summary-cols">
              <div>
                <h4>Identified Risks</h4>
                <ul><li *ngFor="let r of s.aiIdentifiedRisks">{{ r }}</li></ul>
              </div>
              <div>
                <h4>Recommended Actions</h4>
                <ul><li *ngFor="let a of s.aiRecommendedActions">{{ a }}</li></ul>
              </div>
            </div>

            <div *ngIf="s.sourceCitations.length > 0" class="citations-box">
              <h4>Source Record Citations</h4>
              <div *ngFor="let c of s.sourceCitations" class="citation-row">
                <span class="badge badge-primary">Consultation #{{ c.consultationId }}</span>
                {{ c.consultationDate | date:'mediumDate' }} • {{ c.doctorName }} — {{ c.clinicalSnippet }}
              </div>
            </div>

            <details class="pii-audit-box">
              <summary>Verify PII-Minimized Payload Sent to AI Engine (Names, Emails, Phones & Addresses Stripped)</summary>
              <pre>{{ s.sanitizedClinicalDataUsed | json }}</pre>
            </details>

            <!-- Live Summary LLM Judge Quality Gate -->
            <div class="summary-judge-row">
              <button type="button" class="btn-judge-pill" (click)="runJudgeAuditOnSummary(s)" [disabled]="s.judgeLoading">
                {{ s.judgeLoading ? '⚖️ Evaluating Rubrics...' : (s.judgeResult ? '⚖️ Re-Run LLM Judge Audit on Summary' : '⚖️ Audit Summary with LLM Judge') }}
              </button>
            </div>

            <div *ngIf="s.judgeResult as j" class="judge-scorecard" [class.judge-passed]="j.passed" [class.judge-failed]="!j.passed">
              <div class="judge-header">
                <div class="judge-title">
                  <span class="judge-icon">{{ j.passed ? '🛡️' : '⚠️' }}</span>
                  <strong>LLM Judge Quality Gate:</strong>
                  <span class="badge" [class.badge-grounded]="j.passed" [class.badge-danger]="!j.passed">
                    {{ j.passed ? 'PASSED (Production Ready)' : 'REGRESSION BLOCKED' }}
                  </span>
                </div>
                <div class="judge-score">
                  Score: <strong>{{ j.overallScore.toFixed(1) }}</strong> / 5.0
                </div>
              </div>

              <div class="judge-metrics-grid">
                <div *ngFor="let met of j.metrics" class="judge-metric-card" [class.metric-pass]="met.passed" [class.metric-fail]="!met.passed">
                  <div class="metric-top">
                    <span class="metric-name">{{ met.metricName }}</span>
                    <span class="metric-score-tag">{{ met.score.toFixed(1) }} / 5.0</span>
                  </div>
                  <div class="metric-progress-bg">
                    <div class="metric-progress-bar" [style.width.%]="(met.score / 5) * 100" [class.bar-pass]="met.passed" [class.bar-fail]="!met.passed"></div>
                  </div>
                  <div class="metric-expl">{{ met.explanation }}</div>
                </div>
              </div>

              <details class="judge-cot-details">
                <summary>View Chain-of-Thought Rubric Critique</summary>
                <pre class="judge-cot-text">{{ j.chainOfThoughtCritique }}</pre>
              </details>
            </div>
          </div>
        </div>

        <!-- TAB 3: RULE-SAFE SYMPTOM TRIAGE -->
        <div *ngIf="mode() === 'triage'">
          <form (ngSubmit)="runTriage()" novalidate>
            <div class="form-group">
              <label class="form-label" for="triage-symptoms">Reported Symptoms *</label>
              <textarea
                id="triage-symptoms"
                name="triageSymptoms"
                class="form-control"
                required
                placeholder="Describe patient symptoms (e.g., crushing chest pain, fever, sprained ankle)..."
                [(ngModel)]="triageSymptomsText"></textarea>
            </div>
            <div class="form-row">
              <div class="form-group">
                <label class="form-label" for="triage-age">Age</label>
                <input id="triage-age" name="triageAge" type="number" min="0" max="130" class="form-control" [(ngModel)]="triageAge" />
              </div>
              <div class="form-group">
                <label class="form-label" for="triage-gender">Gender</label>
                <select id="triage-gender" name="triageGender" class="form-select" [(ngModel)]="triageGender">
                  <option value="">Unspecified</option>
                  <option value="Female">Female</option>
                  <option value="Male">Male</option>
                </select>
              </div>
              <div class="form-group">
                <label class="form-label" for="triage-cond">Existing Conditions</label>
                <input id="triage-cond" name="triageCond" class="form-control" [(ngModel)]="triageConditions" />
              </div>
            </div>
            <button type="submit" class="btn btn-primary" [disabled]="triageLoading() || !triageSymptomsText.trim()">
              {{ triageLoading() ? 'Evaluating...' : 'Evaluate Triage Urgency' }}
            </button>
          </form>

          <div *ngIf="triageResult() as t" class="triage-card" [class.emergency]="t.urgencyLevel === 'Emergency'">
            <div class="summary-header">
              <h3>Urgency: {{ t.urgencyLevel }} — {{ t.recommendedSpecialty }}</h3>
              <span *ngIf="t.ruleBasedEmergencyTriggered" class="badge badge-danger">Deterministic Red-Flag Rule Matched</span>
            </div>
            <p class="summary-body">{{ t.triageNotes }}</p>
            <div class="summary-cols">
              <div>
                <h4>Red-Flag Questions</h4>
                <ul><li *ngFor="let q of t.redFlagQuestions">{{ q }}</li></ul>
              </div>
              <div>
                <h4>Suggested Next Steps</h4>
                <ul><li *ngFor="let step of t.suggestedNextSteps">{{ step }}</li></ul>
              </div>
            </div>
          </div>
        </div>

        <!-- TAB 4: PYTHON FASTAPI AI MICROSERVICE WORKBENCH -->
        <div *ngIf="mode() === 'microservice'" class="fastapi-workbench">
          <!-- 1. Live Microservice Health Banner -->
          <div class="fastapi-health-banner">
            <div class="fastapi-health-info">
              <span [class.pulse-dot]="fastApiHealth()" [class.pulse-dot-danger]="!fastApiHealth()"></span>
              <strong>FastAPI Microservice Engine:</strong>
              <span *ngIf="fastApiHealth() as h" class="badge badge-grounded">
                ONLINE • {{ h.service }} (v{{ h.version }})
              </span>
              <span *ngIf="!fastApiHealth() && !fastApiHealthLoading()" class="badge badge-danger">
                OFFLINE / UNREACHABLE (:8000)
              </span>
              <span *ngIf="fastApiEngine() as eng" class="muted">
                Model: <strong>{{ eng.model }}</strong> • Uptime: {{ eng.uptimeSeconds.toFixed(0) }}s
              </span>
            </div>
            <button type="button" class="btn btn-sm btn-secondary" (click)="checkFastApiHealth()" [disabled]="fastApiHealthLoading()">
              {{ fastApiHealthLoading() ? 'Pinging...' : '🔄 Ping Microservice' }}
            </button>
          </div>

          <!-- 2. Pydantic v2 Structured Extraction Tester -->
          <div class="microservice-section">
            <div class="section-title-row">
              <div>
                <h3>1. Pydantic v2 Clinical Entity Extraction & Guardrails</h3>
                <p class="muted">Parses unstructured text into validated models with automatic drug-drug contraindication alerts.</p>
              </div>
              <div class="preset-pill-group">
                <span class="muted" style="font-size: 0.75rem;">1-Click Presets:</span>
                <button type="button" class="btn-preset" (click)="setExtractionPreset('contraindication')">⚠️ Warfarin + NSAID</button>
                <button type="button" class="btn-preset" (click)="setExtractionPreset('cardiac')">🚨 Cardiac Emergency</button>
                <button type="button" class="btn-preset" (click)="setExtractionPreset('routine')">🩺 Routine Vitals</button>
              </div>
            </div>

            <div class="form-group">
              <label class="form-label" for="fastapi-text">Raw Clinical Narrative *</label>
              <textarea
                id="fastapi-text"
                class="form-control"
                rows="4"
                placeholder="Enter clinical notes, patient vitals, and prescribed medications..."
                [(ngModel)]="rawClinicalExtractionText"></textarea>
            </div>

            <button type="button" class="btn btn-primary" (click)="extractEntitiesViaFastApi()" [disabled]="extractionLoading() || rawClinicalExtractionText.length < 10">
              {{ extractionLoading() ? 'Extracting via FastAPI...' : '⚡ Extract Entities (Pydantic v2)' }}
            </button>

            <!-- Extracted Record Card -->
            <div *ngIf="extractedRecord() as rec" class="summary-result" style="margin-top: 1rem;">
              <div class="summary-header">
                <div>
                  <strong>Urgency Assessment:</strong>
                  <span class="badge" [class.badge-danger]="rec.urgencyLevel === 'emergency'" [class.badge-warning]="rec.urgencyLevel === 'urgent' || rec.urgencyLevel === 'moderate'" [class.badge-grounded]="rec.urgencyLevel === 'routine'">
                    {{ rec.urgencyLevel | uppercase }}
                  </span>
                  <span *ngIf="rec.requiresImmediateEscalation" class="badge badge-danger" style="margin-left: 0.5rem;">
                    🚨 Immediate Escalation Required
                  </span>
                </div>
                <span class="muted">{{ rec.extractionTimestamp | date:'mediumTime' }}</span>
              </div>

              <!-- Vitals Grid -->
              <h4 style="margin-top: 0.75rem; font-size: 0.88rem;">Validated Numerical Vitals (Systolic > Diastolic Guardrail)</h4>
              <div class="vitals-grid">
                <div class="vital-stat-card">
                  <div class="vital-stat-label">Blood Pressure</div>
                  <div class="vital-stat-val">{{ rec.vitals.systolicBp || '--' }}/{{ rec.vitals.diastolicBp || '--' }}</div>
                  <div class="vital-stat-sub">mmHg</div>
                </div>
                <div class="vital-stat-card">
                  <div class="vital-stat-label">Heart Rate</div>
                  <div class="vital-stat-val">{{ rec.vitals.heartRateBpm || '--' }}</div>
                  <div class="vital-stat-sub">bpm</div>
                </div>
                <div class="vital-stat-card">
                  <div class="vital-stat-label">Blood Glucose</div>
                  <div class="vital-stat-val">{{ rec.vitals.bloodGlucoseMgDl || '--' }}</div>
                  <div class="vital-stat-sub">mg/dL</div>
                </div>
                <div class="vital-stat-card">
                  <div class="vital-stat-label">HbA1c</div>
                  <div class="vital-stat-val">{{ rec.vitals.hba1cPercent || '--' }}%</div>
                  <div class="vital-stat-sub">fraction</div>
                </div>
              </div>

              <!-- Diagnosed Conditions -->
              <div *ngIf="rec.diagnosedConditions.length > 0" style="margin-top: 0.75rem;">
                <h4 style="font-size: 0.88rem;">Diagnosed Conditions</h4>
                <div style="display: flex; gap: 0.4rem; flex-wrap: wrap;">
                  <span *ngFor="let cond of rec.diagnosedConditions" class="badge badge-secondary">🩺 {{ cond }}</span>
                </div>
              </div>

              <!-- Medications with Contraindications -->
              <div *ngIf="rec.medications.length > 0" style="margin-top: 0.75rem;">
                <h4 style="font-size: 0.88rem;">Prescribed Medications & Safety Screen</h4>
                <div *ngFor="let med of rec.medications">
                  <div style="font-size: 0.85rem; font-weight: 600; margin-top: 0.35rem;">
                    💊 {{ med.medicationName }} <span *ngIf="med.dosage">({{ med.dosage }})</span>
                  </div>
                  <div *ngIf="med.contraindicationWarning" class="contraindication-card">
                    <strong>⚠️ CRITICAL CONTRAINDICATION DETECTED</strong>
                    {{ med.contraindicationWarning }}
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- 3. Asynchronous Document Ingestion & Chunking Workbench -->
          <div class="microservice-section">
            <div class="section-title-row">
              <div>
                <h3>2. Background Document Chunking & Vector Ingestion</h3>
                <p class="muted">Submits clinical documents via HTTP 202 Accepted for sliding-window chunking and embedding previews.</p>
              </div>
              <button type="button" class="btn-preset" (click)="loadSampleDocContent()">📄 Load Sample Clinical Note</button>
            </div>

            <div class="form-row">
              <div class="form-group">
                <label class="form-label" for="doc-id">Document ID</label>
                <input id="doc-id" class="form-control" [(ngModel)]="ingestDocId" />
              </div>
              <div class="form-group">
                <label class="form-label" for="doc-type">Document Type</label>
                <select id="doc-type" class="form-select" [(ngModel)]="ingestDocType">
                  <option value="DischargeSummary">Discharge Summary</option>
                  <option value="LabReport">Laboratory Report</option>
                  <option value="ProgressNote">Clinical Progress Note</option>
                </select>
              </div>
              <div class="form-group">
                <label class="form-label" for="chunk-size">Chunk Size (chars)</label>
                <input id="chunk-size" type="number" min="50" max="2000" class="form-control" [(ngModel)]="ingestChunkSize" />
              </div>
              <div class="form-group">
                <label class="form-label" for="chunk-overlap">Overlap (chars)</label>
                <input id="chunk-overlap" type="number" min="0" max="500" class="form-control" [(ngModel)]="ingestChunkOverlap" />
              </div>
            </div>

            <div class="form-group">
              <label class="form-label" for="doc-content">Document Content *</label>
              <textarea
                id="doc-content"
                class="form-control"
                rows="4"
                placeholder="Paste clinical document text for background sliding-window chunking..."
                [(ngModel)]="ingestContent"></textarea>
            </div>

            <div style="display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap;">
              <button type="button" class="btn btn-purple" (click)="queueDocumentIngestion()" [disabled]="ingestLoading() || ingestContent.length < 10">
                {{ ingestLoading() ? 'Queuing Ingest...' : '⚡ Queue Background Ingestion (HTTP 202)' }}
              </button>
              <button *ngIf="activeJobId() as jId" type="button" class="btn btn-sm btn-secondary" (click)="fetchJobStatus(jId)" [disabled]="jobPolling()">
                {{ jobPolling() ? 'Refreshing...' : '🔄 Refresh Job #' + jId }}
              </button>
            </div>

            <!-- Job Output Card -->
            <div *ngIf="jobStatus() as job" class="summary-result" style="margin-top: 1rem;">
              <div class="summary-header">
                <div>
                  <strong>Job Status:</strong>
                  <span class="badge" [class.badge-grounded]="job.status === 'completed'" [class.badge-warning]="job.status === 'processing' || job.status === 'queued'" [class.badge-danger]="job.status === 'failed'">
                    {{ job.status | uppercase }}
                  </span>
                  <span class="badge badge-secondary" style="margin-left: 0.4rem;">{{ job.chunksCount }} Chunks Generated</span>
                </div>
                <span class="muted">Job ID: <code>{{ job.jobId }}</code></span>
              </div>

              <!-- Chunks List -->
              <div *ngIf="job.chunks && job.chunks.length > 0" class="chunk-list">
                <div *ngFor="let ch of job.chunks" class="chunk-card">
                  <div class="chunk-header">
                    <span>Chunk #{{ ch.chunkIndex + 1 }}</span>
                    <span>Offsets: {{ ch.charStart }} — {{ ch.charEnd }} ({{ ch.content.length }} chars)</span>
                  </div>
                  <div class="chunk-body">{{ ch.content }}</div>
                  <div class="chunk-vector-preview">
                    <strong>8-Dim Embedding Vector Preview:</strong>
                    [{{ ch.embeddingPreview.join(', ') }}]
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>
    </div>
  `,
  styles: [`
    .ai-layout { display: flex; flex-direction: column; gap: 0.85rem; }
    .banner-simulated { background: #fef3c7; border: 2px solid #f59e0b; color: #92400e; padding: 0.85rem 1rem; border-radius: var(--radius-md); font-size: 0.88rem; }
    .banner-disclaimer { background: #eff6ff; border: 1px solid #bfdbfe; color: #1e40af; padding: 0.75rem 1rem; border-radius: var(--radius-md); font-size: 0.82rem; }
    .panel { background: white; border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 1.5rem; box-shadow: var(--shadow-sm); }
    .panel-header { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1.25rem; flex-wrap: wrap; }
    .mode-tabs { display: flex; gap: 0.5rem; flex-wrap: wrap; }
    .muted { color: var(--text-muted); font-size: 0.84rem; }
    .context-bar { display: flex; justify-content: space-between; align-items: flex-end; gap: 1rem; margin-bottom: 1rem; flex-wrap: wrap; }
    .inline-group { min-width: 280px; margin-bottom: 0; }
    .session-pill { font-size: 0.78rem; color: var(--text-muted); }
    .chat-feed { border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--bg-page); padding: 1rem; min-height: 260px; max-height: 440px; overflow-y: auto; display: flex; flex-direction: column; gap: 0.85rem; margin-bottom: 1rem; }
    .chat-bubble { padding: 0.85rem 1rem; border-radius: var(--radius-md); max-width: 85%; }
    .chat-bubble.user { align-self: flex-end; background: var(--primary-light); border: 1px solid #bae6fd; }
    .chat-bubble.model { align-self: flex-start; background: white; border: 1px solid var(--border); }
    .bubble-role { font-size: 0.75rem; font-weight: 700; margin-bottom: 0.3rem; display: flex; gap: 0.5rem; align-items: center; }
    .bubble-text { white-space: pre-wrap; font-size: 0.9rem; }
    .meta-line { margin-top: 0.5rem; display: flex; flex-wrap: wrap; gap: 0.35rem; font-size: 0.75rem; color: var(--text-muted); }
    .proposal-card { margin-top: 0.75rem; padding: 0.75rem; border-radius: var(--radius-md); background: #f5f3ff; border: 1px solid #c4b5fd; }
    .proposal-title { font-weight: 700; font-size: 0.82rem; color: #5b21b6; margin-bottom: 0.25rem; }
    .proposal-actions { display: flex; gap: 0.5rem; margin-top: 0.5rem; }
    .swarm-line { align-items: center; margin-top: 0.4rem; }
    .swarm-title { font-weight: 700; color: #1e3a8a; }
    .badge-swarm { background: #dbeafe; color: #1d4ed8; font-weight: 600; padding: 0.15rem 0.45rem; border-radius: 9999px; }
    .exemplar-line { align-items: center; margin-top: 0.35rem; }
    .exemplar-title { font-weight: 700; color: #0d9488; }
    .badge-exemplar { background: #ccfbf1; color: #0f766e; border: 1px solid #99f6e4; font-weight: 600; padding: 0.15rem 0.45rem; border-radius: 9999px; }
    .pharmacovigilance-card { margin-top: 0.75rem; padding: 0.75rem 1rem; border-radius: var(--radius-md); background: #fef2f2; border: 1.5px solid #f87171; color: #991b1b; font-size: 0.82rem; }
    .pv-title { font-weight: 800; font-size: 0.84rem; margin-bottom: 0.35rem; color: #b91c1c; }
    .pharmacovigilance-card ul { margin: 0; padding-left: 1.2rem; }
    .quick-prompts-row { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.75rem; flex-wrap: wrap; }
    .quick-label { font-size: 0.78rem; font-weight: 700; color: #6b21a8; }
    .btn-quick-chip { background: #faf5ff; border: 1px solid #d8b4fe; color: #6b21a8; border-radius: 9999px; padding: 0.25rem 0.65rem; font-size: 0.75rem; font-weight: 600; cursor: pointer; transition: all 0.15s ease; }
    .btn-quick-chip:hover { background: #f3e8ff; border-color: #c084fc; transform: translateY(-1px); }
    .chat-input-row { display: flex; gap: 0.75rem; }
    .summary-controls { display: grid; grid-template-columns: 1fr 1.2fr auto; gap: 0.75rem; align-items: end; margin-bottom: 1.25rem; }
    @media (max-width: 800px) { .summary-controls { grid-template-columns: 1fr; } }
    .summary-result, .triage-card { border: 1px solid var(--border); border-radius: var(--radius-md); padding: 1.25rem; background: var(--bg-page); margin-top: 1rem; }
    .triage-card.emergency { border: 2px solid #dc2626; background: #fef2f2; }
    .summary-header { display: flex; justify-content: space-between; align-items: center; gap: 0.75rem; margin-bottom: 0.75rem; flex-wrap: wrap; }
    .summary-badges-group { display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; }
    .grounding-line { align-items: center; margin-top: 0.35rem; }
    .badge-grounded { background: #dcfce7; color: #15803d; border: 1px solid #86efac; font-weight: 600; }
    .badge-self-corrected { background: #fef08a; color: #854d0e; border: 1px solid #facc15; font-weight: 600; }
    .badge-grounding-warning { background: #fee2e2; color: #b91c1c; border: 1px solid #fca5a5; font-weight: 600; }
    .btn-judge-pill { background: #fdf4ff; border: 1px solid #f0abfc; color: #a21caf; border-radius: 9999px; padding: 0.2rem 0.65rem; font-size: 0.76rem; font-weight: 600; cursor: pointer; transition: all 0.15s ease; margin-top: 0.4rem; }
    .btn-judge-pill:hover:not(:disabled) { background: #fae8ff; border-color: #e879f9; transform: translateY(-1px); }
    .btn-judge-pill:disabled { opacity: 0.6; cursor: not-allowed; }
    .judge-action-row { margin-top: 0.4rem; }
    .summary-judge-row { margin-top: 1rem; margin-bottom: 0.5rem; }
    .judge-scorecard { margin-top: 0.75rem; border-radius: var(--radius-md); padding: 0.85rem 1rem; border: 1.5px solid var(--border); background: white; font-size: 0.82rem; }
    .judge-scorecard.judge-passed { border-color: #86efac; background: #f0fdf4; }
    .judge-scorecard.judge-failed { border-color: #fca5a5; background: #fef2f2; }
    .judge-header { display: flex; justify-content: space-between; align-items: center; gap: 0.5rem; margin-bottom: 0.6rem; flex-wrap: wrap; }
    .judge-title { display: flex; align-items: center; gap: 0.4rem; font-size: 0.85rem; }
    .judge-icon { font-size: 1rem; }
    .judge-score { font-size: 0.85rem; color: #374151; }
    .judge-metrics-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 0.6rem; margin-bottom: 0.6rem; }
    .judge-metric-card { background: white; border: 1px solid var(--border); border-radius: var(--radius-sm); padding: 0.5rem 0.65rem; }
    .judge-metric-card.metric-pass { border-color: #bbf7d0; }
    .judge-metric-card.metric-fail { border-color: #fecaca; background: #fff5f5; }
    .metric-top { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.25rem; font-weight: 600; font-size: 0.78rem; }
    .metric-score-tag { font-size: 0.75rem; color: #4b5563; }
    .metric-progress-bg { height: 5px; background: #e5e7eb; border-radius: 9999px; overflow: hidden; margin-bottom: 0.35rem; }
    .metric-progress-bar { height: 100%; border-radius: 9999px; transition: width 0.3s ease; }
    .metric-progress-bar.bar-pass { background: #22c55e; }
    .metric-progress-bar.bar-fail { background: #ef4444; }
    .metric-expl { font-size: 0.72rem; color: #4b5563; line-height: 1.25; }
    .judge-cot-details { font-size: 0.78rem; margin-top: 0.5rem; }
    .judge-cot-details summary { cursor: pointer; color: #6b21a8; font-weight: 600; }
    .judge-cot-text { background: white; border: 1px solid var(--border); border-radius: var(--radius-sm); padding: 0.5rem 0.75rem; font-size: 0.74rem; white-space: pre-wrap; margin-top: 0.35rem; color: #374151; max-height: 160px; overflow-y: auto; }
    .summary-body { margin-bottom: 1rem; }
    .summary-cols { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem; }
    .summary-cols ul { padding-left: 1.25rem; font-size: 0.88rem; }
    .citations-box { margin-top: 0.75rem; }
    .citation-row { font-size: 0.82rem; margin-top: 0.35rem; }
    .pii-audit-box { margin-top: 1rem; font-size: 0.82rem; }
    .pii-audit-box pre { background: white; padding: 0.75rem; border-radius: var(--radius-sm); border: 1px solid var(--border); overflow-x: auto; margin-top: 0.5rem; }
    .form-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 0.75rem; }
    .alert { padding: 0.75rem 1rem; border-radius: var(--radius-md); font-size: 0.85rem; margin-bottom: 1rem; }
    .alert-danger { background: var(--danger-light); color: #b91c1c; border: 1px solid #fecaca; }
    .alert-success { background: var(--success-light); color: #047857; border: 1px solid #a7f3d0; }
    .sr-only { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0,0,0,0); border: 0; }
    .fastapi-workbench { display: flex; flex-direction: column; gap: 1.25rem; }
    .fastapi-health-banner { display: flex; justify-content: space-between; align-items: center; padding: 0.85rem 1.15rem; background: #0f172a; color: #f8fafc; border-radius: var(--radius-md); box-shadow: var(--shadow-sm); flex-wrap: wrap; gap: 0.75rem; }
    .fastapi-health-info { display: flex; align-items: center; gap: 0.65rem; flex-wrap: wrap; font-size: 0.85rem; }
    .pulse-dot { width: 10px; height: 10px; border-radius: 50%; background: #22c55e; box-shadow: 0 0 8px #22c55e; display: inline-block; }
    .pulse-dot-danger { width: 10px; height: 10px; border-radius: 50%; background: #ef4444; box-shadow: 0 0 8px #ef4444; display: inline-block; }
    .microservice-section { background: white; border: 1px solid var(--border); border-radius: var(--radius-md); padding: 1.25rem; box-shadow: var(--shadow-sm); }
    .section-title-row { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 1rem; flex-wrap: wrap; }
    .preset-pill-group { display: flex; align-items: center; gap: 0.4rem; flex-wrap: wrap; }
    .btn-preset { background: #f1f5f9; border: 1px solid #cbd5e1; border-radius: 9999px; padding: 0.25rem 0.65rem; font-size: 0.74rem; font-weight: 600; color: #334155; cursor: pointer; transition: all 0.15s ease; }
    .btn-preset:hover { background: #e2e8f0; border-color: #94a3b8; }
    .btn-purple { background: #7c3aed; color: white; border: none; padding: 0.45rem 1rem; border-radius: var(--radius-md); font-size: 0.82rem; font-weight: 600; cursor: pointer; }
    .btn-purple:hover:not(:disabled) { background: #6d28d9; }
    .btn-purple:disabled { opacity: 0.6; cursor: not-allowed; }
    .vitals-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(130px, 1fr)); gap: 0.65rem; margin: 0.75rem 0; }
    .vital-stat-card { background: #f8fafc; border: 1px solid var(--border); border-radius: var(--radius-sm); padding: 0.5rem 0.75rem; text-align: center; }
    .vital-value { font-size: 1.25rem; font-weight: 700; color: var(--text-heading); }
    .vital-label { font-size: 0.72rem; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.04em; }
    .contraindication-card { background: #fff1f2; border: 1.5px solid #f43f5e; border-radius: var(--radius-sm); padding: 0.5rem 0.75rem; color: #9f1239; font-size: 0.8rem; margin-top: 0.4rem; }
    .chunk-list { display: flex; flex-direction: column; gap: 0.75rem; margin-top: 0.85rem; }
    .chunk-card { background: white; border: 1px solid var(--border); border-radius: var(--radius-sm); padding: 0.75rem; }
    .chunk-header { display: flex; justify-content: space-between; font-size: 0.76rem; font-weight: 700; color: var(--primary); margin-bottom: 0.4rem; }
    .chunk-body { font-size: 0.84rem; line-height: 1.4; color: #1e293b; white-space: pre-wrap; background: #f8fafc; padding: 0.5rem; border-radius: 4px; margin-bottom: 0.4rem; }
    .chunk-vector-preview { font-size: 0.72rem; color: #64748b; font-family: monospace; word-break: break-all; }
  `]
})
export class AiAgentWorkspaceComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  readonly mode = signal<'chat' | 'summary' | 'triage' | 'microservice'>('chat');
  readonly aiStatus = signal<AiStatusInfo | null>(null);
  readonly patients = signal<PatientSummary[]>([]);
  readonly messages = signal<AiChatMessage[]>([]);
  readonly sessionId = signal<string>('');
  readonly chatStreaming = signal(false);
  readonly summaryLoading = signal(false);
  readonly liveSummary = signal<LivePatientSummary | null>(null);
  readonly triageLoading = signal(false);
  readonly triageResult = signal<AiTriageResponse | null>(null);
  readonly errorMessage = signal('');
  readonly actionFeedback = signal('');

  // FastAPI AI Microservice Workbench State
  readonly fastApiHealth = signal<PythonAiHealth | null>(null);
  readonly fastApiEngine = signal<PythonAiEngineStatus | null>(null);
  readonly fastApiHealthLoading = signal<boolean>(false);

  rawClinicalExtractionText = '';
  readonly extractionLoading = signal<boolean>(false);
  readonly extractedRecord = signal<PythonClinicalRecord | null>(null);

  ingestDocId = 'DOC-CLINICAL-' + Math.floor(1000 + Math.random() * 9000);
  ingestDocType = 'DischargeSummary';
  ingestChunkSize = 250;
  ingestChunkOverlap = 40;
  ingestContent = '';
  readonly ingestLoading = signal<boolean>(false);
  readonly activeJobId = signal<string | null>(null);
  readonly jobStatus = signal<PythonJobStatus | null>(null);
  readonly jobPolling = signal<boolean>(false);

  selectedPatientId: number | null = null;
  chatInput = '';
  summaryInstructions = '';
  triageSymptomsText = '';
  triageAge: number | null = 45;
  triageGender = '';
  triageConditions = '';

  ngOnInit(): void {
    const qpPatientId = this.route.snapshot.queryParamMap.get('patientId');
    if (qpPatientId) {
      this.selectedPatientId = Number(qpPatientId);
      this.mode.set('summary');
    }

    this.api.getAiStatus().subscribe({
      next: res => this.aiStatus.set(res.data),
      error: err => this.errorMessage.set(extractApiErrorMessage(err))
    });

    this.api.getPatients({ pageSize: 50 }).subscribe({
      next: res => {
        const list = res.data?.items || [];
        this.patients.set(list);
        if (!this.selectedPatientId && list.length > 0) {
          this.selectedPatientId = list[0].id;
        }
      }
    });
  }

  sendChatStream(): void {
    const prompt = this.chatInput.trim();
    if (!prompt || this.chatStreaming()) return;

    this.errorMessage.set('');
    this.actionFeedback.set('');
    this.chatInput = '';
    this.chatStreaming.set(true);

    const modelMsg: AiChatMessage = { role: 'model', content: '' };
    this.messages.update(list => [...list, { role: 'user', content: prompt }, modelMsg]);

    this.api.streamChatWithAgent(prompt, this.sessionId() || undefined, this.selectedPatientId).subscribe({
      next: evt => {
        if (evt.type === 'meta') {
          if (evt.data.sessionId) this.sessionId.set(evt.data.sessionId);
          modelMsg.toolsExecuted = evt.data.toolsExecuted;
          modelMsg.pendingActions = evt.data.pendingActions;
          modelMsg.sourceCitations = evt.data.sourceCitations;
          modelMsg.collaboratingAgents = evt.data.collaboratingAgents;
          modelMsg.pharmacovigilanceWarnings = evt.data.pharmacovigilanceWarnings;
          modelMsg.injectedExemplars = evt.data.injectedExemplars;
          modelMsg.groundingScore = evt.data.groundingScore;
          modelMsg.isGrounded = evt.data.isGrounded;
          modelMsg.wasSelfCorrected = evt.data.wasSelfCorrected;
          modelMsg.isSimulatedMode = evt.data.isSimulatedMode;
          modelMsg.emergencyRuleTriggered = evt.data.emergencyRuleTriggered;
        } else if (evt.type === 'token' && evt.data.text) {
          modelMsg.content += evt.data.text;
        }
        this.messages.update(list => [...list]);
      },
      error: err => {
        this.chatStreaming.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      },
      complete: () => {
        this.chatStreaming.set(false);
      }
    });
  }

  setQuickPrompt(promptText: string): void {
    this.chatInput = promptText;
  }

  confirmProposal(act: PendingActionProposal): void {
    this.errorMessage.set('');
    this.api.confirmPendingAiAction(act.actionId).subscribe({
      next: res => {
        act.status = 'Confirmed';
        this.actionFeedback.set(`Consultation #${res.data.id} confirmed and scheduled with ${res.data.doctorName}.`);
      },
      error: err => this.errorMessage.set(extractApiErrorMessage(err))
    });
  }

  cancelProposal(act: PendingActionProposal): void {
    this.api.cancelPendingAiAction(act.actionId).subscribe({
      next: () => {
        act.status = 'Cancelled';
        this.actionFeedback.set('Proposed consultation action cancelled.');
      },
      error: err => this.errorMessage.set(extractApiErrorMessage(err))
    });
  }

  generateSummary(): void {
    if (!this.selectedPatientId) return;
    this.summaryLoading.set(true);
    this.errorMessage.set('');
    this.api.getLivePatientSummary(this.selectedPatientId, this.summaryInstructions).subscribe({
      next: res => {
        this.liveSummary.set(res.data);
        this.summaryLoading.set(false);
      },
      error: err => {
        this.summaryLoading.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }

  runTriage(): void {
    if (!this.triageSymptomsText.trim()) return;
    this.triageLoading.set(true);
    this.errorMessage.set('');
    this.api.triageSymptoms(this.triageSymptomsText, this.triageAge, this.triageGender, this.triageConditions).subscribe({
      next: res => {
        this.triageResult.set(res.data);
        this.triageLoading.set(false);
      },
      error: err => {
        this.triageLoading.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }

  runJudgeAudit(msg: AiChatMessage, queryContext?: string): void {
    if (!msg.content || msg.judgeLoading) return;
    msg.judgeLoading = true;

    const sourceContext = msg.sourceCitations?.map(c => c.clinicalSnippet).filter(Boolean).join('\n---\n')
      || (this.selectedPatientId ? `Active patient ID #${this.selectedPatientId}` : 'Clinical Consultation Record');

    const query = queryContext || 'Clinical inquiry review and verification';

    this.api.evaluateAiResponse({
      query,
      sourceContext,
      generatedResponse: msg.content
    }).subscribe({
      next: res => {
        msg.judgeResult = res.data;
        msg.judgeLoading = false;
        this.messages.update(list => [...list]);
      },
      error: err => {
        msg.judgeLoading = false;
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }

  runJudgeAuditOnSummary(summary: LivePatientSummary): void {
    if (!summary.aiGeneratedSummary || summary.judgeLoading) return;
    summary.judgeLoading = true;

    const source = summary.sourceCitations?.map(c => c.clinicalSnippet).filter(Boolean).join('\n---\n')
      || JSON.stringify(summary.sanitizedClinicalDataUsed || {});

    this.api.evaluateAiResponse({
      query: `Generate de-identified clinical summary for ${summary.patientLabel}`,
      sourceContext: source,
      generatedResponse: summary.aiGeneratedSummary
    }).subscribe({
      next: res => {
        summary.judgeResult = res.data;
        summary.judgeLoading = false;
        this.liveSummary.set({ ...summary });
      },
      error: err => {
        summary.judgeLoading = false;
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }

  checkFastApiHealth(): void {
    this.fastApiHealthLoading.set(true);
    this.api.getPythonAiHealth().subscribe({
      next: res => {
        this.fastApiHealth.set(res.data);
        this.api.getPythonAiEngineStatus().subscribe({
          next: engRes => {
            this.fastApiEngine.set(engRes.data);
            this.fastApiHealthLoading.set(false);
          },
          error: () => {
            this.fastApiHealthLoading.set(false);
          }
        });
      },
      error: () => {
        this.fastApiHealth.set(null);
        this.fastApiEngine.set(null);
        this.fastApiHealthLoading.set(false);
      }
    });
  }

  setExtractionPreset(preset: 'contraindication' | 'cardiac' | 'routine'): void {
    if (preset === 'contraindication') {
      this.rawClinicalExtractionText =
        'Patient Jane Doe (DOB 1968-05-12) presented for acute lower back sprain. ' +
        'Current active medications include Warfarin 5mg daily for chronic atrial fibrillation. ' +
        'Prescribed Ibuprofen 800mg TID for pain relief. Patient reports mild nausea. ' +
        'BP 138/86 mmHg, Pulse 76 bpm, Glucose 105 mg/dL.';
    } else if (preset === 'cardiac') {
      this.rawClinicalExtractionText =
        'EMERGENCY: Patient John Smith, 62yo male, severe crushing substernal chest pain radiating to left jaw, diaphoresis, shortness of breath for 45 minutes. ' +
        'Vitals: BP 85/50 mmHg, HR 125 bpm (sinus tachycardia), SpO2 91% on room air. ' +
        'Immediate escalation required for suspected acute STEMI.';
    } else {
      this.rawClinicalExtractionText =
        'Routine quarterly diabetic checkup for 54yo female. Diagnosed Type 2 Diabetes Mellitus and Essential Hypertension. ' +
        'Current meds: Metformin 1000mg BID, Lisinopril 20mg daily. ' +
        'Vitals: BP 124/80 mmHg, Pulse 72 bpm, Fasting Blood Glucose 118 mg/dL, HbA1c 6.8%.';
    }
  }

  extractEntitiesViaFastApi(): void {
    const text = this.rawClinicalExtractionText.trim();
    if (text.length < 10 || this.extractionLoading()) return;

    this.extractionLoading.set(true);
    this.errorMessage.set('');
    this.api.extractClinicalRecordViaPython(text, this.selectedPatientId ?? undefined).subscribe({
      next: res => {
        this.extractedRecord.set(res.data);
        this.extractionLoading.set(false);
      },
      error: err => {
        this.extractionLoading.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }

  loadSampleDocContent(): void {
    this.ingestDocId = 'DOC-CLINICAL-' + Math.floor(1000 + Math.random() * 9000);
    this.ingestContent =
      'PATIENT CLINICAL DISCHARGE SUMMARY & CARE CONTINUITY PLAN\n\n' +
      'Patient was admitted with acute exacerbation of Congestive Heart Failure (NYHA Class III) and uncontrolled Type 2 Diabetes Mellitus. ' +
      'During the 4-day inpatient stay, intravenous furosemide 40mg twice daily was administered, resulting in 4.2 kg net fluid diuresis. ' +
      'Serial basic metabolic panels demonstrated stabilization of serum creatinine at 1.1 mg/dL and potassium at 4.3 mEq/L.\n\n' +
      'DISCHARGE MEDICATIONS & PROTOCOL:\n' +
      '1. Sacubitril/Valsartan (Entresto) 24/26 mg orally twice daily with meals.\n' +
      '2. Metoprolol Succinate extended-release 50 mg orally once daily in morning.\n' +
      '3. Empagliflozin (Jardiance) 10 mg orally once daily for cardio-renal protection.\n' +
      '4. Furosemide 20 mg orally once daily as maintenance diuretic.\n\n' +
      'RED-FLAG WARNING INSTRUCTIONS:\n' +
      'Patient advised to monitor daily morning weights prior to breakfast. ' +
      'Notify cardiology clinic immediately or present to nearest emergency department if weight increases by >3 lbs in 24 hours or >5 lbs in one week, ' +
      'or upon emergence of orthopnea, nocturnal dyspnea, or bilateral peripheral pedal edema.';
  }

  queueDocumentIngestion(): void {
    const content = this.ingestContent.trim();
    if (content.length < 10 || this.ingestLoading()) return;

    this.ingestLoading.set(true);
    this.errorMessage.set('');
    const patientId = this.selectedPatientId || 1;

    this.api.ingestDocumentViaPython(
      this.ingestDocId,
      patientId,
      this.ingestDocType,
      content,
      this.ingestChunkSize,
      this.ingestChunkOverlap
    ).subscribe({
      next: res => {
        this.activeJobId.set(res.data.jobId);
        this.ingestLoading.set(false);
        this.fetchJobStatus(res.data.jobId);
      },
      error: err => {
        this.ingestLoading.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }

  fetchJobStatus(jobId: string): void {
    if (!jobId || this.jobPolling()) return;

    this.jobPolling.set(true);
    this.api.getPythonDocumentJobStatus(jobId).subscribe({
      next: res => {
        this.jobStatus.set(res.data);
        this.jobPolling.set(false);
      },
      error: err => {
        this.jobPolling.set(false);
        this.errorMessage.set(extractApiErrorMessage(err));
      }
    });
  }
}
