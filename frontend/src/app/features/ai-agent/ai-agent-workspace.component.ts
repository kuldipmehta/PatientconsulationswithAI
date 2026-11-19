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
  PendingActionProposal
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
  `]
})
export class AiAgentWorkspaceComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  readonly mode = signal<'chat' | 'summary' | 'triage'>('chat');
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
}
