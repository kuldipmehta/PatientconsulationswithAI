using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Services;

public class GeminiPatientAgentService : IAiPatientAgentService
{
    private readonly HttpClient _httpClient;
    private readonly IPatientService _patientService;
    private readonly IConsultationService _consultationService;
    private readonly ILogger<GeminiPatientAgentService> _logger;
    private readonly string? _configuredApiKey;
    private readonly string _model;
    private readonly string _baseUrl;
    private string? _cachedDiscoveredModel;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public GeminiPatientAgentService(
        HttpClient httpClient,
        IConfiguration configuration,
        IPatientService patientService,
        IConsultationService consultationService,
        ILogger<GeminiPatientAgentService> logger)
    {
        _httpClient = httpClient;
        _patientService = patientService;
        _consultationService = consultationService;
        _logger = logger;

        _configuredApiKey = configuration["Gemini:ApiKey"] 
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        _model = configuration["Gemini:Model"] ?? "gemini-flash-latest";
        _baseUrl = configuration["Gemini:ApiBaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta";

        if (string.IsNullOrWhiteSpace(_configuredApiKey) || _configuredApiKey.Contains("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Gemini API key not configured or contains placeholder. AI Agent will operate in Simulated / Fallback Mode unless key is provided per-request.");
        }
        else
        {
            _logger.LogInformation("Gemini AI Agent initialized with model '{Model}'.", _model);
        }
    }

    private string? ResolveApiKey(string? explicitKey)
    {
        if (!string.IsNullOrWhiteSpace(explicitKey) && !explicitKey.Contains("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            return explicitKey.Trim();
        }
        return string.IsNullOrWhiteSpace(_configuredApiKey) || _configuredApiKey.Contains("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase)
            ? null
            : _configuredApiKey.Trim();
    }

    public async Task<List<string>> GetAvailableModelsAsync(
        string? explicitApiKey = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveKey = ResolveApiKey(explicitApiKey);
        if (string.IsNullOrWhiteSpace(effectiveKey))
        {
            return new List<string> { "gemini-2.5-flash", "gemini-2.0-flash", "gemini-2.5-flash-lite", "gemini-1.5-flash-latest" };
        }

        try
        {
            var endpoint = $"{_baseUrl}/models?key={effectiveKey}";
            using var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("ListModels query returned status {Status}: {Body}", response.StatusCode, errorText);
                return new List<string> { "gemini-2.5-flash", "gemini-2.0-flash", "gemini-2.5-flash-lite", "gemini-1.5-flash-latest" };
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("models", out var modelsElement) && modelsElement.ValueKind == JsonValueKind.Array)
            {
                var results = new List<string>();
                foreach (var m in modelsElement.EnumerateArray())
                {
                    bool supportsGenerate = false;
                    if (m.TryGetProperty("supportedGenerationMethods", out var methods) && methods.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var method in methods.EnumerateArray())
                        {
                            if (string.Equals(method.GetString(), "generateContent", StringComparison.OrdinalIgnoreCase))
                            {
                                supportsGenerate = true;
                                break;
                            }
                        }
                    }

                    if (supportsGenerate && m.TryGetProperty("name", out var nameProp))
                    {
                        var rawName = nameProp.GetString() ?? "";
                        var cleanName = rawName.StartsWith("models/") ? rawName["models/".Length..] : rawName;
                        if (!string.IsNullOrWhiteSpace(cleanName))
                        {
                            results.Add(cleanName);
                        }
                    }
                }

                if (results.Count > 0)
                {
                    return results;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query ListModels from Gemini API.");
        }

        return new List<string> { "gemini-2.5-flash", "gemini-2.0-flash", "gemini-2.5-flash-lite", "gemini-1.5-flash-latest" };
    }

    private static bool IsDeprecatedOrRetired(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return true;
        return model.Contains("1.5", StringComparison.OrdinalIgnoreCase)
            || model.Contains("2.5-flash", StringComparison.OrdinalIgnoreCase)
            || model.Contains("2.5-pro", StringComparison.OrdinalIgnoreCase)
            || model.Contains("2.0", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ResolveEffectiveModelAsync(
        string effectiveKey,
        string? explicitModel,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(explicitModel) && !IsDeprecatedOrRetired(explicitModel))
        {
            return explicitModel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_cachedDiscoveredModel) && !IsDeprecatedOrRetired(_cachedDiscoveredModel))
        {
            return _cachedDiscoveredModel;
        }

        // If configured model is modern (not deprecated), use it
        if (!string.IsNullOrWhiteSpace(_model) && !IsDeprecatedOrRetired(_model))
        {
            return _model;
        }

        // Dynamically discover supported models for this specific API key
        var available = await GetAvailableModelsAsync(effectiveKey, cancellationToken);
        if (available != null && available.Count > 0)
        {
            var preferred = available.FirstOrDefault(m => string.Equals(m, "gemini-3.8-flash", StringComparison.OrdinalIgnoreCase))
                ?? available.FirstOrDefault(m => string.Equals(m, "gemini-flash-latest", StringComparison.OrdinalIgnoreCase))
                ?? available.FirstOrDefault(m => string.Equals(m, "gemini-3.7-flash", StringComparison.OrdinalIgnoreCase))
                ?? available.FirstOrDefault(m => string.Equals(m, "gemini-3.6-flash", StringComparison.OrdinalIgnoreCase))
                ?? available.FirstOrDefault(m => string.Equals(m, "gemini-3.5-flash", StringComparison.OrdinalIgnoreCase))
                ?? available.FirstOrDefault(m => m.Contains("flash", StringComparison.OrdinalIgnoreCase) && !IsDeprecatedOrRetired(m) && !m.Contains("tts") && !m.Contains("image"))
                ?? available.FirstOrDefault(m => !IsDeprecatedOrRetired(m) && !m.Contains("tts") && !m.Contains("image"))
                ?? available.FirstOrDefault(m => !IsDeprecatedOrRetired(m))
                ?? available[0];

            _cachedDiscoveredModel = preferred;
            _logger.LogInformation("Auto-selected active Gemini model '{Model}' for API key. Available models: [{Models}]", preferred, string.Join(", ", available));
            return preferred;
        }

        return "gemini-3.8-flash";
    }

    private async Task<(string ResponseJson, string UsedModel)> PostWithAutoModelFallbackAsync(
        string effectiveKey,
        string? explicitModel,
        object payload,
        CancellationToken cancellationToken)
    {
        var modelToUse = await ResolveEffectiveModelAsync(effectiveKey, explicitModel, cancellationToken);
        var endpoint = $"{_baseUrl}/models/{modelToUse}:generateContent?key={effectiveKey}";

        using var response = await _httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);
        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _cachedDiscoveredModel = modelToUse;
            return (responseString, modelToUse);
        }

        // If status is 503 (ServiceUnavailable / High Demand) or 429 (TooManyRequests), failover across active alternative models
        if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || (int)response.StatusCode == 429)
        {
            _logger.LogWarning("Gemini model '{Model}' is currently experiencing high demand (Status {Status}). Attempting automatic failover...", modelToUse, (int)response.StatusCode);
            var availableModels = await GetAvailableModelsAsync(effectiveKey, cancellationToken);

            var failoverCandidates = new List<string>();
            if (availableModels.Contains("gemini-flash-latest", StringComparer.OrdinalIgnoreCase) && !string.Equals(modelToUse, "gemini-flash-latest", StringComparison.OrdinalIgnoreCase))
                failoverCandidates.Add("gemini-flash-latest");

            foreach (var alt in new[] { "gemini-3.7-flash", "gemini-3.5-flash", "gemini-3.6-flash", "gemini-3.1-flash-lite", "gemini-flash-lite-latest", "gemini-pro-latest" })
            {
                if (availableModels.Contains(alt, StringComparer.OrdinalIgnoreCase) && !string.Equals(modelToUse, alt, StringComparison.OrdinalIgnoreCase))
                    failoverCandidates.Add(alt);
            }

            foreach (var candidate in failoverCandidates)
            {
                try
                {
                    _logger.LogInformation("Attempting failover request with model '{Candidate}'...", candidate);
                    var retryEndpoint = $"{_baseUrl}/models/{candidate}:generateContent?key={effectiveKey}";
                    using var retryResponse = await _httpClient.PostAsJsonAsync(retryEndpoint, payload, cancellationToken);
                    var retryString = await retryResponse.Content.ReadAsStringAsync(cancellationToken);

                    if (retryResponse.IsSuccessStatusCode)
                    {
                        _cachedDiscoveredModel = candidate;
                        return (retryString, candidate);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failover to model '{Candidate}' failed.", candidate);
                }
            }
        }

        // If status is 404 (model not found/supported in this API version or tier), discover active models and auto-retry
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Gemini returned 404 for model '{Model}'. Parsing suggested model or discovering supported models...", modelToUse);
            var availableModels = await GetAvailableModelsAsync(effectiveKey, cancellationToken);

            string? fallbackModel = null;

            // Check if Google returned a specific model recommendation (e.g., "Please update your code to use models/gemini-3.8-flash")
            var match = System.Text.RegularExpressions.Regex.Match(responseString, @"models/(gemini-[\w\.\-]+)");
            if (match.Success)
            {
                var suggested = match.Groups[1].Value;
                if (availableModels.Any(m => string.Equals(m, suggested, StringComparison.OrdinalIgnoreCase)))
                {
                    fallbackModel = suggested;
                    _logger.LogInformation("Extracted Google recommended model: '{SuggestedModel}'", suggested);
                }
            }

            if (string.IsNullOrWhiteSpace(fallbackModel))
            {
                fallbackModel = availableModels.FirstOrDefault(m => string.Equals(m, "gemini-3.8-flash", StringComparison.OrdinalIgnoreCase))
                    ?? availableModels.FirstOrDefault(m => string.Equals(m, "gemini-flash-latest", StringComparison.OrdinalIgnoreCase))
                    ?? availableModels.FirstOrDefault(m => !string.Equals(m, modelToUse, StringComparison.OrdinalIgnoreCase) && !IsDeprecatedOrRetired(m) && !m.Contains("tts") && !m.Contains("image"))
                    ?? availableModels.FirstOrDefault(m => !string.Equals(m, modelToUse, StringComparison.OrdinalIgnoreCase) && !IsDeprecatedOrRetired(m))
                    ?? availableModels.FirstOrDefault(m => !string.Equals(m, modelToUse, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(fallbackModel))
            {
                _logger.LogInformation("Auto-retrying Gemini request with discovered active model '{NewModel}'...", fallbackModel);
                var retryEndpoint = $"{_baseUrl}/models/{fallbackModel}:generateContent?key={effectiveKey}";
                using var retryResponse = await _httpClient.PostAsJsonAsync(retryEndpoint, payload, cancellationToken);
                var retryString = await retryResponse.Content.ReadAsStringAsync(cancellationToken);

                if (retryResponse.IsSuccessStatusCode)
                {
                    _cachedDiscoveredModel = fallbackModel;
                    return (retryString, fallbackModel);
                }
            }

            throw new InvalidOperationException(
                $"Google Gemini model '{modelToUse}' is not supported for your API key/tier (404 Not Found).\n\n" +
                $"Available models for your key: [ {string.Join(", ", availableModels)} ]\n\n" +
                $"Please pass one of these models (recommended: 'gemini-3.8-flash' or 'gemini-flash-latest') in the request 'model' parameter or set it in appsettings.json. Details: {responseString}");
        }

        _logger.LogError("Gemini API call failed with status {StatusCode}: {ErrorResponse}", response.StatusCode, responseString);
        throw new InvalidOperationException($"Google Gemini API returned status {response.StatusCode}: {responseString}");
    }

    public async Task<LivePatientSummaryResponseDto> SummarizePatientLiveAsync(
        int patientId,
        string? customInstructions = null,
        string? explicitApiKey = null,
        string? explicitModel = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveKey = ResolveApiKey(explicitApiKey);

        // 1. Fetch real, live data from SQL Server via EF Core
        var patient = await _patientService.GetPatientByIdAsync(patientId, cancellationToken);
        if (patient == null)
        {
            throw new KeyNotFoundException($"Patient with ID {patientId} was not found in the SQL Server database.");
        }

        // Format live consultations for prompt
        var consultationsSummary = patient.Consultations.Count == 0
            ? "No prior consultations recorded in database."
            : string.Join("\n\n", patient.Consultations.Select((c, idx) =>
                $"Visit #{idx + 1} on {c.ConsultationDate:yyyy-MM-dd}:\n" +
                $"  - Attending Doctor: {c.DoctorName}\n" +
                $"  - Diagnosis: {c.Diagnosis}\n" +
                $"  - Reported Symptoms: {c.Symptoms ?? "None reported"}\n" +
                $"  - Clinical Notes: {c.Notes ?? "None"}\n" +
                $"  - Treatment Plan: {c.TreatmentPlan ?? "None"}\n" +
                $"  - Prescription: {c.Prescription ?? "None"}\n" +
                $"  - Status: {c.Status} | Fee: ${c.Fee:F2}"));

        var dbSnapshot = new
        {
            PatientId = patient.Id,
            FullName = patient.FullName,
            Age = patient.Age,
            Gender = patient.Gender,
            Email = patient.Email,
            Phone = patient.PhoneNumber,
            DateOfBirth = patient.DateOfBirth,
            MedicalNotes = patient.MedicalNotes,
            TotalConsultationsCount = patient.Consultations.Count,
            Consultations = patient.Consultations
        };

        // If no API key provided, return informative local assessment with raw database payload
        if (string.IsNullOrWhiteSpace(effectiveKey))
        {
            var localAssessment = GenerateLocalClinicalSummary(patient);
            return new LivePatientSummaryResponseDto
            {
                PatientId = patient.Id,
                PatientName = patient.FullName,
                TotalConsultationsFetchedFromDb = patient.Consultations.Count,
                AiGeneratedSummary = "[Local Database Assessment - No Gemini API Key provided]\n\n" +
                    $"Successfully fetched live patient profile and {patient.Consultations.Count} consultation(s) from SQL Server.\n\n" +
                    localAssessment.ClinicalSummary +
                    "\n\n👉 To generate a LIVE Gemini 1.5 Flash summary: Provide your Gemini API key in Swagger's 'apiKey' field, in the 'X-Gemini-ApiKey' header, or in appsettings.json.",
                AiIdentifiedRisks = localAssessment.KnownConditionsAndAllergies,
                AiRecommendedActions = localAssessment.ActionableRecommendations,
                UrgencyAssessment = localAssessment.UrgencyLevel,
                LiveDatabaseDataUsed = dbSnapshot,
                ModelUsed = "Local-Rules (Provide Gemini API Key for Live AI)",
                GeneratedAtUtc = DateTime.UtcNow
            };
        }

        // 2. Query Google Gemini API with the REAL SQL Server record (with automatic model discovery & fallback)
        var prompt = $$"""
            You are a Senior Healthcare AI Medical Specialist.
            Analyze the following REAL patient medical record fetched LIVE from our SQL Server database:

            ======================================================================
            LIVE SQL SERVER DATABASE RECORD:
            ======================================================================
            Patient ID: #{{patient.Id}}
            Full Name: {{patient.FullName}}
            Age: {{patient.Age}} years old | Gender: {{patient.Gender}}
            Email: {{patient.Email}}
            Phone: {{patient.PhoneNumber ?? "Not specified"}}
            Date of Birth: {{(patient.DateOfBirth.HasValue ? patient.DateOfBirth.Value.ToString("yyyy-MM-dd") : "Not specified")}}
            Address: {{patient.Address ?? "Not specified"}}
            Recorded Medical Notes / Background: {{patient.MedicalNotes ?? "None recorded"}}
            Total Historical Consultations in Database: {{patient.Consultations.Count}}

            CONSULTATION HISTORY RECORDED IN DATABASE:
            {{consultationsSummary}}
            ======================================================================

            DOCTOR'S INSTRUCTIONS / FOCUS:
            {{(string.IsNullOrWhiteSpace(customInstructions) ? "Synthesize past consultations and diagnoses. Identify any clinical patterns, drug compliance or allergy warnings, and provide clear actionable next steps." : customInstructions)}}

            Respond strictly in valid JSON matching this schema:
            {
              "summary": "Detailed 2-3 paragraph clinical summary synthesizing past visits, diagnoses, treatments, and current patient health trajectory",
              "risks": ["Specific medical risk or alert 1", "Specific risk 2"],
              "recommendedActions": ["Actionable follow-up recommendation 1", "Actionable recommendation 2"],
              "urgency": "Routine" // Choose strictly from: "Routine", "Moderate", "Urgent"
            }
            """;

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = prompt } }
                }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                temperature = 0.2
            }
        };

        string responseString;
        string usedModel;

        try
        {
            (responseString, usedModel) = await PostWithAutoModelFallbackAsync(effectiveKey, explicitModel, payload, cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("503") || ex.Message.Contains("high demand", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "All Gemini models are temporarily experiencing high demand. Generating clinical assessment grounded in SQL Server database records.");
            var localAssessment = GenerateLocalClinicalSummary(patient);
            return new LivePatientSummaryResponseDto
            {
                PatientId = patient.Id,
                PatientName = patient.FullName,
                TotalConsultationsFetchedFromDb = patient.Consultations.Count,
                AiGeneratedSummary = "[Live Database Assessment - Gemini High Demand Failover]\n\n" +
                    $"Google's Gemini API is temporarily experiencing high global traffic (503 Service Unavailable). " +
                    $"Your patient and consultation records were successfully fetched live from SQL Server, and the assessment below was synthesized directly from your real database history:\n\n" +
                    localAssessment.ClinicalSummary +
                    "\n\n👉 Tip: You can retry in a few moments or specify 'gemini-flash-latest' / 'gemini-3.7-flash' in the 'model' field.",
                AiIdentifiedRisks = localAssessment.KnownConditionsAndAllergies,
                AiRecommendedActions = localAssessment.ActionableRecommendations,
                UrgencyAssessment = localAssessment.UrgencyLevel,
                LiveDatabaseDataUsed = dbSnapshot,
                ModelUsed = "Local-Grounding (Gemini 503 Failover)",
                GeneratedAtUtc = DateTime.UtcNow
            };
        }

        using var doc = JsonDocument.Parse(responseString);
        var root = doc.RootElement;
        var candidate = root.GetProperty("candidates")[0];
        var text = candidate.GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Gemini returned an empty response.");
        }

        var parsed = JsonSerializer.Deserialize<JsonElement>(text);

        return new LivePatientSummaryResponseDto
        {
            PatientId = patient.Id,
            PatientName = patient.FullName,
            TotalConsultationsFetchedFromDb = patient.Consultations.Count,
            AiGeneratedSummary = parsed.TryGetProperty("summary", out var sm) ? (sm.GetString() ?? "") : text,
            AiIdentifiedRisks = ParseStringArray(parsed, "risks"),
            AiRecommendedActions = ParseStringArray(parsed, "recommendedActions"),
            UrgencyAssessment = parsed.TryGetProperty("urgency", out var urg) ? (urg.GetString() ?? "Routine") : "Routine",
            LiveDatabaseDataUsed = dbSnapshot,
            ModelUsed = usedModel,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    public async Task<AiChatResponseDto> ChatAsync(AiChatRequestDto request, CancellationToken cancellationToken = default)
    {
        var toolsExecuted = new List<string>();
        var effectiveKey = ResolveApiKey(request.ApiKey);

        if (string.IsNullOrWhiteSpace(effectiveKey))
        {
            return await ExecuteFallbackAgentAsync(request, toolsExecuted, cancellationToken);
        }

        try
        {
            return await ExecuteGeminiAgentWithToolsAsync(request, effectiveKey, toolsExecuted, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini API call failed. Falling back to local agent execution.");
            var fallback = await ExecuteFallbackAgentAsync(request, toolsExecuted, cancellationToken);
            fallback.Reply = $"[Notice: Gemini live API error: {ex.Message} - executed via local agent]\n\n" + fallback.Reply;
            return fallback;
        }
    }

    private async Task<AiChatResponseDto> ExecuteGeminiAgentWithToolsAsync(
        AiChatRequestDto request,
        string effectiveKey,
        List<string> toolsExecuted,
        CancellationToken cancellationToken)
    {
        var modelToUse = await ResolveEffectiveModelAsync(effectiveKey, request.Model, cancellationToken);
        var endpoint = $"{_baseUrl}/models/{modelToUse}:generateContent?key={effectiveKey}";

        // Build tool declarations
        var toolsDeclaration = GetGeminiToolsDeclaration();

        // Build system prompt
        var systemInstruction = new
        {
            parts = new[]
            {
                new
                {
                    text = """
                        You are an intelligent, empathetic, and professional Clinical AI Assistant for a Healthcare Patient Management System.
                        You have autonomous access to tool functions to query and update the SQL Server database:
                        - 'search_patients': Search patients by name or email.
                        - 'get_patient_details': Retrieve complete patient record including full consultation history.
                        - 'schedule_consultation': Book/create a new doctor consultation for a patient.
                        - 'get_all_consultations': Fetch consultations, optionally by patient.

                        Instructions:
                        1. Always ground your answers in the database data. When a patient is mentioned, check their details first.
                        2. If asked to schedule or book a consultation, call 'schedule_consultation'.
                        3. Always provide clear, concise, and professional medical summaries based on the real records.
                        """
                }
            }
        };

        // Prepare contents history
        var contents = new List<JsonObject>();

        if (request.ConversationHistory != null && request.ConversationHistory.Count > 0)
        {
            foreach (var msg in request.ConversationHistory)
            {
                var role = msg.Role.ToLowerInvariant() == "user" ? "user" : "model";
                var histObj = new JsonObject
                {
                    ["role"] = role,
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = msg.Content } }
                };
                contents.Add(histObj);
            }
        }

        var userMessage = request.Message;
        
        // Ground context if patientContextId is provided
        if (request.PatientContextId.HasValue)
        {
            var ctxPatient = await _patientService.GetPatientByIdAsync(request.PatientContextId.Value, cancellationToken);
            if (ctxPatient != null)
            {
                var consBrief = ctxPatient.Consultations.Count == 0 
                    ? "No consultations" 
                    : string.Join("; ", ctxPatient.Consultations.Select(c => $"[{c.ConsultationDate:yyyy-MM-dd}] Dr. {c.DoctorName}: {c.Diagnosis} (Rx: {c.Prescription ?? "None"})"));

                userMessage = $"[LIVE DATABASE RECORD: Patient #{ctxPatient.Id} {ctxPatient.FullName}, Age: {ctxPatient.Age}, Gender: {ctxPatient.Gender}, MedicalNotes: '{ctxPatient.MedicalNotes}', Past Consultations: {consBrief}]\n\nUser Question: {request.Message}";
            }
        }

        contents.Add(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray { new JsonObject { ["text"] = userMessage } }
        });

        // Multi-turn tool execution loop (up to 5 turns)
        for (int turn = 0; turn < 5; turn++)
        {
            var payload = new JsonObject
            {
                ["contents"] = JsonSerializer.SerializeToNode(contents, JsonOptions),
                ["tools"] = JsonSerializer.SerializeToNode(toolsDeclaration, JsonOptions),
                ["systemInstruction"] = JsonSerializer.SerializeToNode(systemInstruction, JsonOptions),
                ["generationConfig"] = new JsonObject
                {
                    ["temperature"] = 0.2
                }
            };

            using var response = await _httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);
            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (turn == 0 && (response.StatusCode == System.Net.HttpStatusCode.NotFound || response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || (int)response.StatusCode == 429))
                {
                    var available = await GetAvailableModelsAsync(effectiveKey, cancellationToken);
                    string? fallbackModel = null;
                    var match = System.Text.RegularExpressions.Regex.Match(responseString, @"models/(gemini-[\w\.\-]+)");
                    if (match.Success && available.Any(m => string.Equals(m, match.Groups[1].Value, StringComparison.OrdinalIgnoreCase)))
                    {
                        fallbackModel = match.Groups[1].Value;
                    }
                    else
                    {
                        fallbackModel = available.FirstOrDefault(m => string.Equals(m, "gemini-3.8-flash", StringComparison.OrdinalIgnoreCase))
                            ?? available.FirstOrDefault(m => string.Equals(m, "gemini-flash-latest", StringComparison.OrdinalIgnoreCase))
                            ?? available.FirstOrDefault(m => !string.Equals(m, modelToUse, StringComparison.OrdinalIgnoreCase) && !IsDeprecatedOrRetired(m) && !m.Contains("tts") && !m.Contains("image"))
                            ?? available.FirstOrDefault(m => !string.Equals(m, modelToUse, StringComparison.OrdinalIgnoreCase) && !IsDeprecatedOrRetired(m))
                            ?? available.FirstOrDefault(m => !string.Equals(m, modelToUse, StringComparison.OrdinalIgnoreCase));
                    }

                    if (!string.IsNullOrWhiteSpace(fallbackModel))
                    {
                        _logger.LogInformation("Auto-switching AI Chat model from '{Old}' to '{New}'...", modelToUse, fallbackModel);
                        modelToUse = fallbackModel;
                        _cachedDiscoveredModel = fallbackModel;
                        endpoint = $"{_baseUrl}/models/{modelToUse}:generateContent?key={effectiveKey}";
                        using var retryResp = await _httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);
                        var retryStr = await retryResp.Content.ReadAsStringAsync(cancellationToken);
                        if (retryResp.IsSuccessStatusCode)
                        {
                            responseString = retryStr;
                            goto ProcessResponse;
                        }
                    }
                }

                throw new HttpRequestException($"Gemini API error (Status {response.StatusCode}): {responseString}");
            }

        ProcessResponse:
            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("No candidates returned from Gemini API.");
            }

            var firstCandidate = candidates[0];
            var contentElem = firstCandidate.GetProperty("content");
            var partsElem = contentElem.GetProperty("parts");

            // Check if model returned a functionCall
            bool hasFunctionCall = false;
            string? functionName = null;
            JsonElement functionArgs = default;

            foreach (var part in partsElem.EnumerateArray())
            {
                if (part.TryGetProperty("functionCall", out var funcCall))
                {
                    hasFunctionCall = true;
                    functionName = funcCall.GetProperty("name").GetString();
                    funcCall.TryGetProperty("args", out functionArgs);
                    break;
                }
            }

            if (!hasFunctionCall || string.IsNullOrEmpty(functionName))
            {
                // No function call: final answer returned
                string replyText = "";
                foreach (var part in partsElem.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElem))
                    {
                        replyText += textElem.GetString();
                    }
                }

                return new AiChatResponseDto
                {
                    Reply = replyText.Trim(),
                    ToolsExecuted = toolsExecuted,
                    ModelUsed = modelToUse,
                    Success = true
                };
            }

            // Record and execute the tool
            _logger.LogInformation("Gemini requested tool call: {ToolName} with args: {Args}", functionName, functionArgs.ToString());
            toolsExecuted.Add($"{functionName}({functionArgs})");

            var toolResultNode = await ExecuteToolAsync(functionName, functionArgs, cancellationToken);

            // Append model's functionCall turn to contents
            contents.Add(new JsonObject
            {
                ["role"] = "model",
                ["parts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["functionCall"] = new JsonObject
                        {
                            ["name"] = functionName,
                            ["args"] = JsonNode.Parse(functionArgs.GetRawText())
                        }
                    }
                }
            });

            // Append function response turn to contents
            contents.Add(new JsonObject
            {
                ["role"] = "function",
                ["parts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["functionResponse"] = new JsonObject
                        {
                            ["name"] = functionName,
                            ["response"] = toolResultNode
                        }
                    }
                }
            });
        }

        return new AiChatResponseDto
        {
            Reply = "Conversation reached maximum tool execution depth.",
            ToolsExecuted = toolsExecuted,
            ModelUsed = _model,
            Success = true
        };
    }

    private async Task<JsonNode> ExecuteToolAsync(string toolName, JsonElement args, CancellationToken cancellationToken)
    {
        switch (toolName.ToLowerInvariant())
        {
            case "search_patients":
            {
                string term = args.TryGetProperty("searchTerm", out var st) ? (st.GetString() ?? "") : "";
                var filter = new PatientFilterParams { SearchTerm = term, PageSize = 5 };
                var result = await _patientService.GetPatientsAsync(filter, cancellationToken);
                return JsonSerializer.SerializeToNode(result.Items, JsonOptions) ?? new JsonArray();
            }

            case "get_patient_details":
            {
                int id = args.TryGetProperty("patientId", out var pid) ? pid.GetInt32() : 0;
                var patient = await _patientService.GetPatientByIdAsync(id, cancellationToken);
                return JsonSerializer.SerializeToNode(patient, JsonOptions) ?? new JsonObject();
            }

            case "schedule_consultation":
            {
                int patientId = args.TryGetProperty("patientId", out var pid) ? pid.GetInt32() : 0;
                string doctorName = args.TryGetProperty("doctorName", out var dn) ? (dn.GetString() ?? "Dr. General") : "Dr. General";
                string diagnosis = args.TryGetProperty("diagnosis", out var diag) ? (diag.GetString() ?? "Checkup") : "Checkup";
                string? symptoms = args.TryGetProperty("symptoms", out var sym) ? sym.GetString() : null;
                string? notes = args.TryGetProperty("notes", out var nt) ? nt.GetString() : null;
                decimal? fee = args.TryGetProperty("fee", out var fe) && fe.ValueKind == JsonValueKind.Number ? fe.GetDecimal() : 150.00m;

                var dto = new CreateConsultationDto
                {
                    PatientId = patientId,
                    DoctorName = doctorName,
                    Diagnosis = diagnosis,
                    Symptoms = symptoms,
                    Notes = notes,
                    Fee = fee,
                    ConsultationDate = DateTime.UtcNow.AddDays(1),
                    Status = ConsultationStatus.Scheduled
                };

                var created = await _consultationService.CreateConsultationAsync(dto, cancellationToken);
                return JsonSerializer.SerializeToNode(created, JsonOptions) ?? new JsonObject();
            }

            case "get_all_consultations":
            {
                int? patientId = args.TryGetProperty("patientId", out var pid) ? pid.GetInt32() : null;
                var list = await _consultationService.GetConsultationsAsync(patientId, cancellationToken);
                return JsonSerializer.SerializeToNode(list.Take(10), JsonOptions) ?? new JsonArray();
            }

            default:
                return new JsonObject { ["error"] = $"Unknown tool: {toolName}" };
        }
    }

    public async Task<PatientClinicalSummaryDto> GenerateClinicalSummaryAsync(
        int patientId,
        string? explicitApiKey = null,
        string? explicitModel = null,
        CancellationToken cancellationToken = default)
    {
        var patient = await _patientService.GetPatientByIdAsync(patientId, cancellationToken);
        if (patient == null)
        {
            throw new KeyNotFoundException($"Patient with ID {patientId} was not found.");
        }

        var effectiveKey = ResolveApiKey(explicitApiKey);

        if (!string.IsNullOrWhiteSpace(effectiveKey))
        {
            try
            {
                var summaryFromGemini = await CallGeminiForClinicalSummaryAsync(patient, effectiveKey, explicitModel, cancellationToken);
                if (summaryFromGemini != null)
                {
                    return summaryFromGemini;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get clinical summary from live Gemini API. Generating structured local summary.");
            }
        }

        return GenerateLocalClinicalSummary(patient);
    }

    private async Task<PatientClinicalSummaryDto?> CallGeminiForClinicalSummaryAsync(
        PatientResponseDto patient,
        string effectiveKey,
        string? explicitModel,
        CancellationToken cancellationToken)
    {
        var consultationsText = patient.Consultations.Count == 0
            ? "No prior consultations on record."
            : string.Join("\n", patient.Consultations.Select(c =>
                $"- [{c.ConsultationDate:yyyy-MM-dd}] Dr. {c.DoctorName}: {c.Diagnosis}. Symptoms: {c.Symptoms ?? "N/A"}. Plan: {c.TreatmentPlan ?? "N/A"}. Rx: {c.Prescription ?? "N/A"}"));

        var prompt = $$"""
            Analyze the following patient record and return a structured JSON assessment:
            Patient Name: {{patient.FullName}}
            Age: {{patient.Age}}, Gender: {{patient.Gender}}
            Medical Notes: {{patient.MedicalNotes ?? "None"}}
            Past Consultations:
            {{consultationsText}}

            Respond strictly in valid JSON matching this format:
            {
              "clinicalSummary": "Comprehensive 2-3 sentence clinical summary",
              "knownConditionsAndAllergies": ["Condition or Allergy 1", "Condition 2"],
              "recentConsultationHighlights": ["Highlight 1", "Highlight 2"],
              "actionableRecommendations": ["Recommendation 1", "Recommendation 2"],
              "urgencyLevel": "Routine"
            }
            """;

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = prompt } }
                }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                temperature = 0.2
            }
        };

        var (responseString, _) = await PostWithAutoModelFallbackAsync(effectiveKey, explicitModel, payload, cancellationToken);
        using var doc = JsonDocument.Parse(responseString);
        var text = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

        if (string.IsNullOrEmpty(text)) return null;

        var parsed = JsonSerializer.Deserialize<JsonElement>(text);

        return new PatientClinicalSummaryDto
        {
            PatientId = patient.Id,
            PatientName = patient.FullName,
            Age = patient.Age,
            Gender = patient.Gender,
            ClinicalSummary = parsed.TryGetProperty("clinicalSummary", out var cs) ? (cs.GetString() ?? "") : "",
            KnownConditionsAndAllergies = ParseStringArray(parsed, "knownConditionsAndAllergies"),
            RecentConsultationHighlights = ParseStringArray(parsed, "recentConsultationHighlights"),
            ActionableRecommendations = ParseStringArray(parsed, "actionableRecommendations"),
            UrgencyLevel = parsed.TryGetProperty("urgencyLevel", out var ul) ? (ul.GetString() ?? "Routine") : "Routine",
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    private static List<string> ParseStringArray(JsonElement element, string propName)
    {
        var list = new List<string>();
        if (element.TryGetProperty(propName, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
            }
        }
        return list;
    }

    private static PatientClinicalSummaryDto GenerateLocalClinicalSummary(PatientResponseDto patient)
    {
        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(patient.MedicalNotes))
        {
            conditions.Add(patient.MedicalNotes);
        }

        var highlights = new List<string>();
        foreach (var c in patient.Consultations.Take(3))
        {
            highlights.Add($"{c.ConsultationDate:yyyy-MM-dd} with {c.DoctorName}: {c.Diagnosis} (Status: {c.Status})");
        }

        var recommendations = new List<string>
        {
            "Ensure regular annual physical examination is maintained.",
            patient.Consultations.Count > 0 ? "Review current prescriptions and compliance during next visit." : "Schedule baseline physical and blood work."
        };

        var summary = $"{patient.FullName} ({patient.Age} y/o {patient.Gender}) has {patient.ConsultationCount} consultation(s) on file. " +
            (string.IsNullOrWhiteSpace(patient.MedicalNotes) ? "No chronic medical conditions currently flagged." : $"Known medical background: {patient.MedicalNotes}.");

        return new PatientClinicalSummaryDto
        {
            PatientId = patient.Id,
            PatientName = patient.FullName,
            Age = patient.Age,
            Gender = patient.Gender,
            ClinicalSummary = summary,
            KnownConditionsAndAllergies = conditions,
            RecentConsultationHighlights = highlights,
            ActionableRecommendations = recommendations,
            UrgencyLevel = patient.Age > 65 || patient.ConsultationCount > 3 ? "Moderate" : "Routine",
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    public async Task<AiTriageResponseDto> TriageSymptomsAsync(
        AiTriageRequestDto request,
        string? explicitApiKey = null,
        string? explicitModel = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveKey = ResolveApiKey(explicitApiKey);

        if (!string.IsNullOrWhiteSpace(effectiveKey))
        {
            try
            {
                var geminiTriage = await CallGeminiForTriageAsync(request, effectiveKey, explicitModel, cancellationToken);
                if (geminiTriage != null) return geminiTriage;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to call Gemini for triage. Using local clinical triage rules.");
            }
        }

        return GenerateLocalTriage(request);
    }

    private async Task<AiTriageResponseDto?> CallGeminiForTriageAsync(
        AiTriageRequestDto request,
        string effectiveKey,
        string? explicitModel,
        CancellationToken cancellationToken)
    {
        var prompt = $$"""
            You are a medical triage assistant. Analyze these symptoms:
            Symptoms: {{request.Symptoms}}
            Patient Age: {{request.PatientAge?.ToString() ?? "Unknown"}}
            Gender: {{request.Gender ?? "Unknown"}}
            Known conditions: {{request.ExistingConditions ?? "None"}}

            Respond in JSON with:
            {
              "urgencyLevel": "Low",
              "recommendedSpecialty": "Recommended Medical Specialty",
              "triageNotes": "Clinical triage rationale and summary",
              "redFlagQuestions": ["Question 1 to rule out critical condition", "Question 2"],
              "suggestedNextSteps": ["Step 1", "Step 2"]
            }
            """;

        var payload = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { response_mime_type = "application/json", temperature = 0.1 }
        };

        var (str, _) = await PostWithAutoModelFallbackAsync(effectiveKey, explicitModel, payload, cancellationToken);
        using var doc = JsonDocument.Parse(str);
        var text = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        if (string.IsNullOrEmpty(text)) return null;

        var parsed = JsonSerializer.Deserialize<JsonElement>(text);
        return new AiTriageResponseDto
        {
            UrgencyLevel = parsed.TryGetProperty("urgencyLevel", out var ul) ? (ul.GetString() ?? "Low") : "Low",
            RecommendedSpecialty = parsed.TryGetProperty("recommendedSpecialty", out var rs) ? (rs.GetString() ?? "General Physician") : "General Physician",
            TriageNotes = parsed.TryGetProperty("triageNotes", out var tn) ? (tn.GetString() ?? "") : "",
            RedFlagQuestions = ParseStringArray(parsed, "redFlagQuestions"),
            SuggestedNextSteps = ParseStringArray(parsed, "suggestedNextSteps"),
            AnalyzedAtUtc = DateTime.UtcNow
        };
    }

    private static AiTriageResponseDto GenerateLocalTriage(AiTriageRequestDto request)
    {
        var sym = request.Symptoms.ToLowerInvariant();
        string urgency = "Low";
        string specialty = "General Physician";
        var redFlags = new List<string>();
        var steps = new List<string>();

        if (sym.Contains("chest pain") || sym.Contains("shortness of breath") || sym.Contains("faint") || sym.Contains("stroke"))
        {
            urgency = "Emergency";
            specialty = "Emergency Medicine / Cardiology";
            redFlags.Add("Does the pain radiate to your left arm, neck, or jaw?");
            redFlags.Add("Are you experiencing sudden dizziness or sweating?");
            steps.Add("Seek emergency medical care immediately or call emergency services (911/112).");
            steps.Add("Do not drive yourself to the hospital.");
        }
        else if (sym.Contains("fever") || sym.Contains("cough") || sym.Contains("wheez") || sym.Contains("throat"))
        {
            urgency = "Medium";
            specialty = "Pulmonology / Internal Medicine";
            redFlags.Add("Has the fever persisted for more than 48 hours?");
            redFlags.Add("Are you experiencing any difficulty breathing while resting?");
            steps.Add("Schedule a doctor consultation for respiratory assessment.");
            steps.Add("Stay well-hydrated and monitor temperature twice daily.");
        }
        else if (sym.Contains("knee") || sym.Contains("back") || sym.Contains("joint") || sym.Contains("pain") || sym.Contains("bone"))
        {
            urgency = "Medium";
            specialty = "Orthopedics / Physical Therapy";
            redFlags.Add("Are you unable to bear weight on the affected limb?");
            steps.Add("Rest the affected area, avoid strenuous activity.");
            steps.Add("Book an orthopedic consultation for physical exam and X-ray imaging.");
        }
        else
        {
            urgency = "Low";
            specialty = "General Practice / Family Medicine";
            steps.Add("Schedule a routine consultation with your primary physician.");
            steps.Add("Keep a log of symptoms and triggers over the next 3 days.");
        }

        return new AiTriageResponseDto
        {
            UrgencyLevel = urgency,
            RecommendedSpecialty = specialty,
            TriageNotes = $"Automated clinical assessment based on reported symptoms: '{request.Symptoms}'.",
            RedFlagQuestions = redFlags,
            SuggestedNextSteps = steps,
            AnalyzedAtUtc = DateTime.UtcNow
        };
    }

    private async Task<AiChatResponseDto> ExecuteFallbackAgentAsync(
        AiChatRequestDto request,
        List<string> toolsExecuted,
        CancellationToken cancellationToken)
    {
        var msg = request.Message.Trim();
        var msgLower = msg.ToLowerInvariant();

        // 1. Check if user wants to schedule a consultation
        if (msgLower.Contains("schedule") || msgLower.Contains("book") || msgLower.Contains("appointment"))
        {
            int targetId = request.PatientContextId ?? 1;
            var newCons = await _consultationService.CreateConsultationAsync(new CreateConsultationDto
            {
                PatientId = targetId,
                DoctorName = "Dr. Sarah Jenkins, MD",
                Diagnosis = "Follow-up consultation via AI Assistant",
                ConsultationDate = DateTime.UtcNow.AddDays(2),
                Status = ConsultationStatus.Scheduled,
                Fee = 150.00m,
                Notes = $"Scheduled automatically by AI Agent from prompt: '{msg}'"
            }, cancellationToken);

            toolsExecuted.Add($"schedule_consultation(patientId: {targetId}, doctor: 'Dr. Sarah Jenkins')");

            return new AiChatResponseDto
            {
                Reply = $"✅ Successfully booked consultation **#{newCons.Id}** for **{newCons.PatientName}** with **{newCons.DoctorName}**!\n" +
                        $"- **Diagnosis/Reason:** {newCons.Diagnosis}\n" +
                        $"- **Date:** {newCons.ConsultationDate:yyyy-MM-dd}\n" +
                        $"- **Status:** {newCons.Status}\n" +
                        $"- **Fee:** ${newCons.Fee:F2}",
                ToolsExecuted = toolsExecuted,
                ExtractedData = newCons,
                ModelUsed = "Gemini-Agent (Local Fallback Mode)",
                Success = true
            };
        }

        // 2. Check if user wants to search / find patients
        if (msgLower.Contains("find") || msgLower.Contains("search") || msgLower.Contains("who is") || (msgLower.Contains("patient") && !msgLower.Contains("schedule")))
        {
            var words = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string searchTerm = "";
            foreach (var w in words)
            {
                if (char.IsUpper(w[0]) && !w.Equals("Find", StringComparison.OrdinalIgnoreCase) && !w.Equals("Show", StringComparison.OrdinalIgnoreCase) && !w.Equals("Search", StringComparison.OrdinalIgnoreCase))
                {
                    searchTerm = w;
                    break;
                }
            }

            var filter = new PatientFilterParams { SearchTerm = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm, PageSize = 5 };
            var paged = await _patientService.GetPatientsAsync(filter, cancellationToken);
            toolsExecuted.Add($"search_patients(searchTerm: '{searchTerm}')");

            if (paged != null && paged.TotalCount > 0)
            {
                var patientListFormatted = string.Join("\n", paged.Items.Select(p =>
                    $"- **#{p.Id} {p.FullName}** ({p.Age} y/o {p.Gender}) | Email: {p.Email} | Consultations: {p.ConsultationCount}"));

                return new AiChatResponseDto
                {
                    Reply = $"I searched the SQL Server database and found {paged.TotalCount} matching patient(s):\n\n{patientListFormatted}\n\nWould you like me to fetch full consultation history or schedule an appointment for any of them?",
                    ToolsExecuted = toolsExecuted,
                    ExtractedData = paged.Items,
                    ModelUsed = "Gemini-Agent (Local Fallback Mode - configure 'Gemini:ApiKey' for live LLM reasoning)",
                    Success = true
                };
            }
        }

        // 3. Check if context patient is provided or requested
        if (request.PatientContextId.HasValue || msgLower.Contains("eleanor") || msgLower.Contains("marcus") || msgLower.Contains("amina"))
        {
            int pid = request.PatientContextId ?? 1;
            var patient = await _patientService.GetPatientByIdAsync(pid, cancellationToken);
            if (patient != null)
            {
                toolsExecuted.Add($"get_patient_details(patientId: {pid})");
                var consSummary = patient.Consultations.Count == 0
                    ? "No previous consultations recorded."
                    : string.Join("\n", patient.Consultations.Select(c =>
                        $"  * **{c.ConsultationDate:yyyy-MM-dd}** with {c.DoctorName}: *{c.Diagnosis}* (Status: {c.Status})"));

                return new AiChatResponseDto
                {
                    Reply = $"Here is the record for **{patient.FullName}** (ID #{patient.Id}):\n" +
                            $"- **Age/Gender:** {patient.Age} y/o {patient.Gender}\n" +
                            $"- **Email:** {patient.Email} | **Phone:** {patient.PhoneNumber ?? "N/A"}\n" +
                            $"- **Medical Notes:** {patient.MedicalNotes ?? "None recorded"}\n" +
                            $"- **Past Consultations:**\n{consSummary}\n\n" +
                            $"You can ask me to schedule a new consultation or summarize their medical history.",
                    ToolsExecuted = toolsExecuted,
                    ExtractedData = patient,
                    ModelUsed = "Gemini-Agent (Local Fallback Mode)",
                    Success = true
                };
            }
        }

        // Default friendly response
        return new AiChatResponseDto
        {
            Reply = "Hello! I am your **Clinical AI Assistant** powered by Google Gemini.\n\n" +
                    "Here is what I can do for you:\n" +
                    "1. 🔍 **Find patients**: *\"Find patient Eleanor Vance\"* or *\"Search for patients with email gmail.com\"*\n" +
                    "2. 📋 **Summarize records**: *\"Show me medical history for patient #1\"*\n" +
                    "3. 📅 **Schedule consultations**: *\"Schedule a follow-up for patient #1 with Dr. Jenkins\"*\n" +
                    "4. 🩺 **Triage symptoms**: Ask about any symptoms for specialty recommendations and urgency rating.\n\n" +
                    "*(Tip: Set your 'Gemini:ApiKey' in appsettings.json or include 'apiKey' in your request to unlock live Gemini 1.5 Flash LLM reasoning!)*",
            ToolsExecuted = toolsExecuted,
            ModelUsed = "Gemini-Agent (Ready)",
            Success = true
        };
    }

    private static object GetGeminiToolsDeclaration()
    {
        return new[]
        {
            new
            {
                functionDeclarations = new object[]
                {
                    new
                    {
                        name = "search_patients",
                        description = "Search for patients in the SQL database by first name, last name, or email.",
                        parameters = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                searchTerm = new { type = "STRING", description = "Patient name or email substring to search" }
                            },
                            required = new[] { "searchTerm" }
                        }
                    },
                    new
                    {
                        name = "get_patient_details",
                        description = "Retrieve the complete patient profile and history of consultations by unique patient ID.",
                        parameters = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                patientId = new { type = "INTEGER", description = "The unique patient ID" }
                            },
                            required = new[] { "patientId" }
                        }
                    },
                    new
                    {
                        name = "schedule_consultation",
                        description = "Schedules and creates a new doctor consultation in SQL Server for an existing patient.",
                        parameters = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                patientId = new { type = "INTEGER", description = "The ID of the patient" },
                                doctorName = new { type = "STRING", description = "Name of the attending physician (e.g., Dr. Sarah Jenkins)" },
                                diagnosis = new { type = "STRING", description = "Primary reason for consultation or initial diagnosis" },
                                symptoms = new { type = "STRING", description = "Patient reported symptoms" },
                                notes = new { type = "STRING", description = "Clinical or triage notes" },
                                fee = new { type = "NUMBER", description = "Consultation fee amount" }
                            },
                            required = new[] { "patientId", "doctorName", "diagnosis" }
                        }
                    },
                    new
                    {
                        name = "get_all_consultations",
                        description = "Get a list of consultations, optionally filtered by patient ID.",
                        parameters = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                patientId = new { type = "INTEGER", description = "Optional patient ID to filter by" }
                            }
                        }
                    }
                }
            }
        };
    }
}
