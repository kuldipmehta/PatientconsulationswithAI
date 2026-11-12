# Privacy, Security & Healthcare Regulatory Compliance Guide

This document describes the technical controls implemented in **CareSync** and the organizational/contractual requirements needed before processing real Protected Health Information (PHI) or Sensitive Personal Data.

---

## 1. Data Minimization & Third-Party AI Governance

All calls to Google Gemini pass through [`PatientAiSanitizer`](src/PatientManagement.Infrastructure/Services/Ai/PatientAiSanitizer.cs):

- **Excluded PII Fields:** First name, last name, email address, phone number, residential street address, profile photo URLs, and database primary credentials are **never serialized** into LLM prompts.
- **Pseudonymized Clinical Context:** Records are transmitted solely using `Patient #<id>`, age, gender, medical notes, and de-identified consultation history referenced by `Consultation #<id>`.
- **Prompt Injection Defense:** User-supplied symptom descriptions and clinician notes are wrapped in `<untrusted_clinical_text>` delimiters with strict system instructions and server-side RBAC enforcement inside C# tool handlers.
- **AI Audit Trail:** Every AI prompt, model identifier, simulated/live flag, tool invocation, and clinician identity is recorded in `AiInteractionLogs`.

### Google Gemini API Data-Use Terms

- **Free / Unpaid Gemini Developer API Tier:** Google terms may allow prompts and responses to be reviewed or used to improve Google products. **Do not send real identifiable patient records over free-tier keys.**
- **Production Healthcare Use:** Configure either a **paid Google AI Studio billing tier** (which disables product-improvement data logging by default under the Paid Services terms) or **Google Cloud Vertex AI** with a signed **Business Associate Agreement (BAA)** and VPC Service Controls / regional data residency.

---

## 2. India — Digital Personal Data Protection (DPDP) Act, 2023

For patients located in India:

- **Lawful Basis & Consent Notice:** Obtain explicit, granular patient consent specifying clinical care and optional AI-assisted clinical decision support prior to onboarding.
- **Purpose Limitation & Minimization:** Only clinical attributes strictly necessary for triage/summarization are processed by AI helpers; direct identifiers stay inside SQL Server and private Azure Blob Storage.
- **Security Safeguards:** Enforced via JWT/Entra ID RBAC, PBKDF2 password hashing, single-use hashed setup tokens, private storage containers with 15-minute SAS URLs, and full `AuditLogs` traceability.
- **Retention & Grievance Redressal:** Soft delete (`IsDeleted`, `DeletedAtUtc`, `DeletedByUserId`) preserves statutory medical retention while hiding deactivated records from active workflows. Add a designated Data Protection Officer / grievance contact workflow for production deployment.

---

## 3. United States — HIPAA Safeguards

If serving Covered Entities or Business Associates in the United States:

- **Business Associate Agreements (BAAs):** Execute BAAs with Microsoft Azure (App Service, Azure SQL, Azure Blob Storage, Key Vault, Application Insights) and Google Cloud (Vertex AI / Enterprise Gemini) before storing or transmitting live ePHI.
- **Administrative & Technical Controls Implemented:**
  - Role-based access control (`Admin`, `Doctor`, `Receptionist`) with physician-level patient scoping.
  - Immutable `AuditLogs` (entity creates, edits, soft deletes) and `AiInteractionLogs`.
  - No credentials or secrets in onboarding emails or client DTOs.
  - Private blob storage containers (`PublicAccessType.None`) with authenticated proxy streaming and short-lived SAS tokens.
  - TLS transport encryption in production (`UseHttpsRedirection`) and environment-driven secret management (Azure Key Vault / environment variables).

---

## 4. Clinical Decision Support Boundary

- Automated triage (`EmergencyTriageRuleEngine` + Gemini) and patient summaries are **Clinical Decision Support** aids only.
- Emergency red flags (acute cardiac chest pain, stroke FAST symptoms, severe dyspnea/anaphylaxis, psychiatric crisis, uncontrolled hemorrhage) short-circuit via deterministic rules before any LLM call and direct immediately to emergency services (`911` / `112` / `108` / `988`).
- AI scheduling actions only create a **Pending Proposal** (`PendingAiActions`) and require explicit human clinician click confirmation (`POST /api/ai/actions/{actionId}/confirm`).
