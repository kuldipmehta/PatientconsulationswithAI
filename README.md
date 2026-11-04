# 🏥 Patient Consultations with AI (.NET 10 + Angular 19 + Google Gemini AI)

A full-stack, production-ready healthcare management solution featuring a **.NET 10 Web API** (Clean Architecture, EF Core, SQL Server, Azure Blob Storage), an autonomous **Google Gemini AI Clinical Agent** with live database function-calling, and a modern **Angular 19 Standalone Dashboard**.

---

## 📋 Table of Contents
1. [Features & Requirements Coverage](#-features--requirements-coverage)
2. [Architecture Overview](#-architecture-overview)
3. [Prerequisites](#-prerequisites)
4. [Getting Started (Local Development)](#-getting-started-local-development)
5. [API Endpoints & Swagger](#-api-endpoints--swagger)
6. [Testing](#-testing)
7. [Azure App Service Deployment (Bonus)](#-azure-app-service-deployment-bonus)
8. [Configuration Guide](#-configuration-guide)

---

## 🎯 Features & Requirements Coverage

| Requirement | Implementation Details | Status |
| :--- | :--- | :---: |
| **Create a Patient** (Basic details + picture in Azure Blob Storage) | `POST /api/patients` accepts `multipart/form-data` with First/Last Name, Email, Phone, Age, Gender, DOB, Address, Notes, and `Picture` file. Picture is processed via Azure Blob Storage SDK (`Azure.Storage.Blobs`), returning a public blob URI. | ✅ |
| **Send Dummy Email** (After patient creation) | `IEmailService` / `DummyEmailService` triggers an automated welcome email with patient credentials, formats structured logs in console, and records an audit trail in SQL Server `EmailAuditLogs`. Viewable at `GET /api/emails/logs`. | ✅ |
| **View Patient List** (.NET 10 API + SQL Server) | `GET /api/patients` queries SQL Server via EF Core, supporting search (name/email), filters (gender, age), sorting, and server-side pagination with consultation summaries. | ✅ |
| **Data Handling** (Fetch & update patient + consultation using EF Core) | Full CRUD for `Patients` and `Consultations` with 1-to-many relationship, cascade rules, indexing, transaction management, and DTO projections. | ✅ |
| **Google Gemini AI Agent** (Autonomous Clinical Assistant & Tools) | Multi-turn conversational AI Agent powered by **Google Gemini** with autonomous tool-calling (queries SQL Server, retrieves records, schedules consultations), patient clinical summaries, and symptom triage. | 🤖 |
| **Bonus Point** (Azure App Service + Swagger Page) | Infrastructure-as-Code (`azure/deploy.bicep` & `azure/azuredeploy.json`), automated deployment script (`deploy-to-azure.ps1`), multi-stage `Dockerfile`, GitHub Actions CI/CD (`.github/workflows/azure-deploy.yml`), and Swagger UI configured for production. | 🌟 |

---

## 🏗️ Architecture Overview

The solution adheres to Clean Architecture and DDD principles, separated into clear layers:

```
d:\Test Task\
│
├── src\
│   ├── PatientManagement.Core\            # Domain Entities, DTOs, Enums, Service Interfaces
│   │   ├── Entities\                      # Patient, Consultation, EmailAuditLog
│   │   ├── DTOs\                          # Patient & Consultation requests, responses, filters
│   │   ├── Enums\                         # ConsultationStatus (Scheduled, Completed, etc.)
│   │   └── Interfaces\                    # IPatientService, IConsultationService, IBlobStorageService, IEmailService
│   │
│   ├── PatientManagement.Infrastructure\  # EF Core, Migrations, Azure Blob Storage, Email Service
│   │   ├── Data\                          # ApplicationDbContext, Fluent API configs, DbInitializer
│   │   │   └── Migrations\                # Auto-generated EF Core SQL Server migrations
│   │   └── Services\                      # AzureBlobStorageService, DummyEmailService, PatientService, ConsultationService
│   │
│   └── PatientManagement.Api\             # ASP.NET Core .NET 10 Web API
│       ├── Controllers\                   # PatientsController, ConsultationsController, EmailsController
│       ├── Middleware\                    # ExceptionHandlingMiddleware
│       ├── Program.cs                     # DI, pipeline, Swagger UI, auto-migration & seeding
│       └── appsettings.json               # SQL Server connection string & Azure configurations
│
├── frontend\                              # Modern Angular 19 Standalone Single Page Application
│   ├── src\app\
│   │   ├── models\                        # TypeScript DTOs & Interfaces
│   │   ├── services\                      # ApiService (Patients, Consultations, AI, Emails)
│   │   └── app.component.*                # Dashboard UI (Patients, AI Clinical Agent, Logs)
│   ├── proxy.conf.json                    # Dev proxy to ASP.NET Core API
│   └── package.json                       # Angular 19 dependencies & scripts
│
├── tests\
│   └── PatientManagement.Tests\           # 12 Unit & Integration Tests (xUnit, Moq, FluentAssertions)
│
├── azure\                                 # Azure App Service IaC & Deployment
│   ├── deploy.bicep                       # Complete Azure resources Bicep template
│   ├── azuredeploy.json                   # ARM template for 1-click Azure deployment
│   ├── deploy-to-azure.ps1                # PowerShell automated deployment script
│   └── deploy-to-azure.sh                 # Bash deployment script
│
├── .github\workflows\azure-deploy.yml     # GitHub Actions CI/CD workflow
├── Dockerfile                             # Multi-stage optimized .NET 10 container
└── PatientManagement.sln                  # Visual Studio / dotnet solution
```

---

## ⚙️ Prerequisites

- **.NET 10 SDK** (v10.0.x or later)
- **SQL Server** (LocalDB `(localdb)\MSSQLLocalDB`, standard `localhost`, or Azure SQL)
- *(Optional)* **Azure CLI (`az`)** for deploying to Azure App Service
- *(Optional)* **Docker Desktop** if running containerized

---

## 🚀 Getting Started (Local Development)

### 1. Clone or Open the Repository
```bash
cd "d:\Test Task"
```

### 2. Configure Database Connection (Optional)
The connection string in `src/PatientManagement.Api/appsettings.json` defaults to SQL Server LocalDB:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=PatientManagementDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
}
```
*(If you prefer a full SQL Server instance, replace `(localdb)\MSSQLLocalDB` with `localhost`).*

### 3. Restore and Build
```bash
dotnet restore
dotnet build
```

### 4. Run the Backend API
```bash
dotnet run --project src/PatientManagement.Api
```

On startup:
- EF Core automatically applies all SQL Server migrations (`Database.MigrateAsync()`).
- Initial sample patient and consultation data is seeded automatically.
- Swagger UI launches at **`http://localhost:5056/`** (or **`https://localhost:7102/swagger`**).

### 5. Run the Angular Frontend Dashboard
```bash
cd frontend
npm start
```

Open your browser at **`http://localhost:4200/`** to interact with the complete web UI:
- **Patient Registry**: Create patients with Azure Blob picture upload, search, filter, paginate, edit, delete.
- **Consultations Management**: Schedule and edit doctor appointments, diagnoses, and prescriptions.
- **AI Clinical Agent**: Run live Gemini clinical assessments grounded in SQL Server records, autonomous database chat, and symptom triage.
- **Email Audit**: View all sent dummy welcome emails and formatted email bodies.

---

## 📡 API Endpoints & Swagger

Swagger UI is served at the root URL **`/`** as well as **`/swagger`**.

### 1. Patients API (`/api/patients`)
| Method | Endpoint | Description | Payload |
| :--- | :--- | :--- | :--- |
| `POST` | `/api/patients` | **Create Patient**: Stores picture in Azure Blob Storage, saves to SQL Server, triggers dummy email. | `multipart/form-data` |
| `GET` | `/api/patients` | **View Patient List**: Filter, search, and paginate patients with consultation counts. | Query parameters (`searchTerm`, `gender`, `pageNumber`, `pageSize`) |
| `GET` | `/api/patients/{id}` | **Fetch Patient**: Patient profile along with their complete consultation history. | - |
| `PUT` | `/api/patients/{id}` | **Update Patient**: Updates patient record and optionally replaces profile picture. | `multipart/form-data` |
| `DELETE` | `/api/patients/{id}` | **Delete Patient**: Cascades delete to consultations and deletes picture from blob storage. | - |

#### Sample: Create Patient Request (`multipart/form-data`)
```bash
curl -X POST "https://localhost:7102/api/patients" \
  -F "FirstName=Jane" \
  -F "LastName=Doe" \
  -F "Email=jane.doe@example.com" \
  -F "PhoneNumber=+1-555-0199" \
  -F "Age=32" \
  -F "Gender=Female" \
  -F "DateOfBirth=1994-06-20" \
  -F "Address=100 Main St, Suite 400" \
  -F "MedicalNotes=No known allergies" \
  -F "Picture=@/path/to/profile.jpg"
```

### 2. Consultations API (`/api/consultations`)
| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `GET` | `/api/consultations?patientId={id}` | Fetch consultations (optionally filtered by patient) |
| `GET` | `/api/consultations/{id}` | Fetch consultation details by ID |
| `POST` | `/api/consultations` | Schedule a new consultation for a patient |
| `PUT` | `/api/consultations/{id}` | Update consultation diagnosis, notes, status, fee |
| `DELETE` | `/api/consultations/{id}` | Delete consultation record |

### 3. Email Audit API (`/api/emails`)
| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `GET` | `/api/emails/logs` | View dispatched dummy welcome emails for audit and verification |

### 4. 🤖 Google Gemini AI Agent API (`/api/ai`)
| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `GET` | `/api/ai/status` | Check AI Agent operational status, active model, and loaded tools |
| `GET` | `/api/ai/models` | List all available active Gemini models supported by your API key |
| `POST` | `/api/ai/patients/{id}/live-summary` | **Live Database Clinical Summary**: Fetches live patient profile & consultations from SQL Server via EF Core, feeds the real data directly to Gemini (`gemini-3.8-flash`), and synthesizes a live summary, clinical risks, and action items |
| `POST` | `/api/ai/chat` | **Autonomous Clinical Agent**: Chat with Gemini to query SQL Server, retrieve patient records, and schedule consultations via function-calling |
| `POST` | `/api/ai/patients/{id}/clinical-summary` | **Clinical Summarizer**: Generates comprehensive clinical summary, past diagnosis highlights, risk factors, and recommendations |
| `POST` | `/api/ai/triage` | **Symptom Triage**: Analyzes reported symptoms, assesses clinical urgency, and suggests medical specialties & red flags |

#### How to Provide Your Gemini API Key:
You can provide your Google Gemini API Key in any of the following ways (no code modifications needed):
1. **Directly in Swagger / Request Body**: Pass `"apiKey": "AIzaSy..."` in the JSON request body.
2. **Via HTTP Request Header**: Include `X-Gemini-ApiKey: AIzaSy...` in your request headers.
3. **In `appsettings.json`**: Set `"Gemini": { "ApiKey": "YOUR_KEY", "Model": "gemini-3.8-flash" }`.
4. **Environment Variable**: Set `GEMINI_API_KEY=AIzaSy...`.

*(Note: The active default model is **`gemini-3.8-flash`**. If any model ever returns 404, the system automatically inspects Google's recommendations and auto-retries with a working model).*

#### Sample: Live Patient Summary Request (`POST /api/ai/patients/{id}/live-summary`)
```json
{
  "customInstructions": "Synthesize past consultations and diagnoses. Focus on cardiovascular health and medication history.",
  "apiKey": "YOUR_GEMINI_API_KEY_HERE"
}
```

#### Sample: AI Agent Chat Request (`POST /api/ai/chat`)
```json
{
  "message": "Find patient Eleanor Vance and summarize what medications and diagnoses were recorded in her last consultation.",
  "apiKey": "YOUR_GEMINI_API_KEY_HERE"
}
```

#### Sample: AI Autonomous Scheduling via Natural Language
```json
{
  "message": "Please book a follow-up consultation for patient #1 with Dr. Sarah Jenkins for allergy review.",
  "patientContextId": 1
}
```

#### Sample: AI Symptom Triage Request (`POST /api/ai/triage`)
```json
{
  "symptoms": "Sudden sharp chest pain radiating to left shoulder and lightheadedness",
  "patientAge": 55,
  "gender": "Male"
}
```

---

## 🧪 Testing

The solution includes **19 comprehensive unit and integration tests** verifying:
- Patient creation with Azure Blob Storage upload and dummy email dispatch.
- Paged querying, sorting, and search filtering on patient records.
- Patient and consultation update and cascading deletion.
- Dummy email formatted body, structured logging, and database auditing.
- Google Gemini AI Agent tool calling (`search_patients`, `schedule_consultation`, `get_patient_details`).
- AI patient clinical summary generation and symptom triage rules.
- End-to-end integration tests using `WebApplicationFactory<Program>`.

Run the test suite with:
```bash
dotnet test
```

Expected output:
```
Passed!  - Failed: 0, Passed: 19, Skipped: 0, Total: 19
```

---

## ☁️ Azure App Service Deployment (Bonus Point)

The API is pre-configured for automated deployment to **Azure App Service** with a live Swagger documentation page.

### Option 1: Automated Deployment Script (PowerShell)
```powershell
cd azure
.\deploy-to-azure.ps1 -ResourceGroupName "rg-patient-management" -Location "eastus"
```
The script will:
1. Authenticate with Azure (`az login`).
2. Provision the Azure App Service (Linux .NET 10), Azure SQL Database, and Azure Blob Storage via Bicep.
3. Build and package the .NET 10 Web API.
4. Deploy the zip artifact to the Azure App Service.
5. Print the live URLs:
   - **App URL:** `https://app-patientmgmt-<id>.azurewebsites.net`
   - **Swagger Page:** `https://app-patientmgmt-<id>.azurewebsites.net/swagger`

### Option 2: Azure CLI / Cloud Shell (Bash)
```bash
cd azure
chmod +x deploy-to-azure.sh
./deploy-to-azure.sh rg-patient-management eastus
```

### Option 3: GitHub Actions CI/CD
A pre-built workflow is located in `.github/workflows/azure-deploy.yml`.
1. Push your repository to GitHub.
2. In GitHub repository settings, add `AZURE_WEBAPP_PUBLISH_PROFILE` secret (downloaded from Azure App Service).
3. Any push to `main` will automatically build, test, and deploy to Azure App Service!

### Option 4: Docker Container Deployment
```bash
# Build image
docker build -t patient-management-api:latest .

# Run container
docker run -d -p 8080:8080 -e "ConnectionStrings__DefaultConnection=Server=host.docker.internal;Database=PatientManagementDb;User Id=sa;Password=YourPassword123!;TrustServerCertificate=True" patient-management-api:latest
```

---

## 🔧 Configuration Guide

`appsettings.json` supports environment-specific overrides or Azure App Settings:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=PatientManagementDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
  },
  "AzureBlobStorage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
    "ContainerName": "patient-pictures"
  },
  "EmailSettings": {
    "SenderEmail": "noreply@patientportal.com",
    "SenderName": "Patient Care Management"
  },
  "Gemini": {
    "ApiKey": "YOUR_GEMINI_API_KEY",
    "Model": "gemini-1.5-flash",
    "ApiBaseUrl": "https://generativelanguage.googleapis.com/v1beta"
  }
}
```

> **Note on Google Gemini AI Agent**:
> - You can obtain a free Gemini API key from [Google AI Studio](https://aistudio.google.com/). Set it in `appsettings.json` under `"Gemini:ApiKey"` or via the `GEMINI_API_KEY` environment variable.
> - **Zero-Config Fallback**: Even without an API key, the AI Agent operates out of the box in an intelligent simulated/fallback mode that queries SQL Server, demonstrates tool execution, generates clinical summaries, and performs symptom triage without throwing any errors!

> **Note on Local Azure Blob Storage**: If no Azure Storage connection string is specified, the application seamlessly uses a local simulated storage provider (`LocalStorage/blobs`) so you can run, create patients, and upload pictures locally without requiring an active Azure subscription. When deployed to Azure, simply supply your Azure Blob Storage connection string or let the Bicep template wire it up automatically!
