# 🏥 CareSync Angular Frontend (Patient Management & AI Clinical Assistant)

This Angular 19 application communicates with the **.NET 10 Web API** to provide an interactive dashboard for managing patients, uploading profile pictures to Azure Blob Storage, managing consultations, auditing dummy welcome emails, and interacting with Google Gemini AI.

---

## 🚀 Quick Start

### 1. Make sure your .NET 10 Backend API is running
```bash
# In project root:
dotnet run --project src/PatientManagement.Api
```
The API serves at `http://localhost:5056` (or `https://localhost:7102`).

### 2. Start the Angular Application
```bash
cd frontend
npm start
```

Open your browser at **`http://localhost:4200`**.

---

## 🌟 Features Included

1. **👥 Patient Registry**:
   - Filter, search, and paginate patients.
   - Register new patient with details (Name, Age, Gender, DOB, Phone, Address, Notes).
   - Upload patient pictures stored directly in **Azure Blob Storage**.
   - Triggers automated dummy welcome email upon registration with audit logging.
   - View, edit, and delete patients (with cascade cleanup).

2. **🩺 Consultations Management**:
   - Schedule new doctor consultations.
   - View, edit, and track statuses (`Scheduled`, `Completed`, `FollowUpRequired`, `Cancelled`).
   - Track diagnoses, reported symptoms, notes, treatment plans, and prescriptions.

3. **✨ Google Gemini AI Clinical Agent**:
   - **Live Database Grounded Summary**: Select any patient in SQL Server and click to synthesize a live clinical assessment using Gemini (`gemini-flash-latest`), complete with identified risks, recommended action items, and verification of the exact raw database record provided to the model.
   - **Autonomous Medical Chat**: Chat with Gemini using natural language to query SQL Server, search patients, inspect medical histories, or schedule appointments autonomously.
   - **Emergency Symptom Triage**: Enter patient symptoms to evaluate clinical urgency, determine medical specialty, and view red flag screening questions.

4. **✉️ Email Audit Logs**:
   - View all dummy welcome emails dispatched upon patient registration.
   - Inspect formatted email body and delivery status.

5. **⚙️ Settings**:
   - Configure API Base URL (defaults to `http://localhost:5056`).
   - Test backend connection health in real time.
   - Select Gemini model from discovered active models for your API key.
