using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Enums;
using PatientManagement.Infrastructure.Services;

namespace PatientManagement.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
        try
        {
            if (!await context.Doctors.AnyAsync())
            {
                var doctors = new List<Doctor>
                {
                    new() { FullName = "Dr. Sarah Jenkins, MD", Specialty = "Internal Medicine", Email = "sarah.jenkins@patientportal.com", StartHourUtc = 9, EndHourUtc = 17, SlotDurationMinutes = 30 },
                    new() { FullName = "Dr. Robert Sterling, Cardiologist", Specialty = "Cardiology", Email = "robert.sterling@patientportal.com", StartHourUtc = 9, EndHourUtc = 17, SlotDurationMinutes = 30 },
                    new() { FullName = "Dr. James Wilson, Orthopedic Specialist", Specialty = "Orthopedics", Email = "james.wilson@patientportal.com", StartHourUtc = 10, EndHourUtc = 18, SlotDurationMinutes = 30 }
                };

                await context.Doctors.AddRangeAsync(doctors);
                await context.SaveChangesAsync();
            }

            var docList = await context.Doctors.OrderBy(d => d.Id).ToListAsync();
            var sarahId = docList.FirstOrDefault()?.Id;
            var robertId = docList.Skip(1).FirstOrDefault()?.Id ?? sarahId;
            var jamesId = docList.Skip(2).FirstOrDefault()?.Id ?? sarahId;

            if (!await context.AppUsers.AnyAsync())
            {
                var users = new List<AppUser>
                {
                    new() { Email = "admin@patientportal.com", FullName = "System Administrator", Role = UserRole.Admin, PasswordHash = PasswordHasher.Hash("Admin@12345"), IsActive = true },
                    new() { Email = "sarah.jenkins@patientportal.com", FullName = "Dr. Sarah Jenkins, MD", Role = UserRole.Doctor, DoctorId = sarahId, PasswordHash = PasswordHasher.Hash("Doctor@12345"), IsActive = true },
                    new() { Email = "robert.sterling@patientportal.com", FullName = "Dr. Robert Sterling", Role = UserRole.Doctor, DoctorId = robertId, PasswordHash = PasswordHasher.Hash("Doctor@12345"), IsActive = true },
                    new() { Email = "reception@patientportal.com", FullName = "Front Desk Reception", Role = UserRole.Receptionist, PasswordHash = PasswordHasher.Hash("Reception@12345"), IsActive = true }
                };

                await context.AppUsers.AddRangeAsync(users);
                await context.SaveChangesAsync();
            }

            if (await context.Patients.IgnoreQueryFilters().AnyAsync())
            {
                // Backfill PrimaryDoctorId on existing seeded patients if missing
                var unassigned = await context.Patients.Where(p => p.PrimaryDoctorId == null).ToListAsync();
                if (unassigned.Count > 0 && sarahId.HasValue)
                {
                    for (int i = 0; i < unassigned.Count; i++)
                    {
                        unassigned[i].PrimaryDoctorId = i % 2 == 0 ? sarahId : robertId;
                    }
                    await context.SaveChangesAsync();
                }

                logger.LogInformation("Database already seeded.");
                return;
            }

            logger.LogInformation("Seeding initial sample patient and consultation records...");

            var patients = new List<Patient>
            {
                new()
                {
                    FirstName = "Eleanor",
                    LastName = "Vance",
                    Email = "eleanor.vance@example.com",
                    PhoneNumber = "+1-555-0101",
                    Age = 34,
                    Gender = "Female",
                    DateOfBirth = new DateTime(1992, 4, 15, 0, 0, 0, DateTimeKind.Utc),
                    Address = "742 Evergreen Terrace, Springfield",
                    MedicalNotes = "Allergic to penicillin. Routine checkups recommended.",
                    ProfilePictureUrl = "https://images.unsplash.com/photo-1544005313-94ddf0286df2?w=400",
                    PrimaryDoctorId = sarahId,
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-30)
                },
                new()
                {
                    FirstName = "Marcus",
                    LastName = "Brody",
                    Email = "marcus.brody@example.com",
                    PhoneNumber = "+1-555-0102",
                    Age = 48,
                    Gender = "Male",
                    DateOfBirth = new DateTime(1978, 9, 21, 0, 0, 0, DateTimeKind.Utc),
                    Address = "221B Baker Street, Marylebone",
                    MedicalNotes = "Mild hypertension. Managed with diet and exercise.",
                    ProfilePictureUrl = "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=400",
                    PrimaryDoctorId = robertId,
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-25)
                },
                new()
                {
                    FirstName = "Amina",
                    LastName = "Rahman",
                    Email = "amina.rahman@example.com",
                    PhoneNumber = "+1-555-0103",
                    Age = 29,
                    Gender = "Female",
                    DateOfBirth = new DateTime(1997, 1, 8, 0, 0, 0, DateTimeKind.Utc),
                    Address = "10 Downing Street, London",
                    MedicalNotes = "Asthma symptoms during seasonal transitions.",
                    ProfilePictureUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=400",
                    PrimaryDoctorId = sarahId,
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-15)
                },
                new()
                {
                    FirstName = "Liam",
                    LastName = "Chen",
                    Email = "liam.chen@example.com",
                    PhoneNumber = "+1-555-0104",
                    Age = 52,
                    Gender = "Male",
                    DateOfBirth = new DateTime(1974, 11, 30, 0, 0, 0, DateTimeKind.Utc),
                    Address = "456 Market St, San Francisco, CA",
                    MedicalNotes = "Previous knee arthroscopy in 2021. Doing physical therapy.",
                    ProfilePictureUrl = "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?w=400",
                    PrimaryDoctorId = jamesId,
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
                }
            };

            await context.Patients.AddRangeAsync(patients);
            await context.SaveChangesAsync();

            var consultations = new List<Consultation>
            {
                new()
                {
                    PatientId = patients[0].Id,
                    DoctorId = sarahId,
                    DoctorName = "Dr. Sarah Jenkins, MD",
                    ConsultationDate = DateTime.UtcNow.AddDays(-28),
                    DurationMinutes = 30,
                    Diagnosis = "General Health Physical & Blood Work",
                    Symptoms = "Annual wellness checkup, mild seasonal allergies",
                    Notes = "Vitals are normal. Prescribed cetirizine for seasonal allergy relief.",
                    TreatmentPlan = "Follow up in 12 months for routine annual exam.",
                    Prescription = "Cetirizine 10mg once daily as needed.",
                    Status = ConsultationStatus.Completed,
                    Fee = 150.00m,
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-28)
                },
                new()
                {
                    PatientId = patients[0].Id,
                    DoctorId = sarahId,
                    DoctorName = "Dr. Sarah Jenkins, MD",
                    ConsultationDate = DateTime.UtcNow.AddDays(-10),
                    DurationMinutes = 30,
                    Diagnosis = "Allergy Follow-up & Preventative Review",
                    Symptoms = "Occasional nasal congestion",
                    Notes = "Symptoms well controlled on antihistamine.",
                    TreatmentPlan = "Continue seasonal prophylaxis.",
                    Prescription = "Fluticasone nasal spray PRN.",
                    Status = ConsultationStatus.Completed,
                    Fee = 160.00m,
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
                },
                new()
                {
                    PatientId = patients[1].Id,
                    DoctorId = robertId,
                    DoctorName = "Dr. Robert Sterling, Cardiologist",
                    ConsultationDate = DateTime.UtcNow.AddDays(-20),
                    DurationMinutes = 30,
                    Diagnosis = "Stage 1 Essential Hypertension Follow-up",
                    Symptoms = "Occasional morning headaches, slightly elevated BP (135/85)",
                    Notes = "ECG normal. Continue sodium restriction and aerobic exercise 4x weekly.",
                    TreatmentPlan = "Monitor blood pressure log weekly. Review in 3 months.",
                    Prescription = "Lisinopril 10mg daily if BP exceeds 140/90 consistently.",
                    Status = ConsultationStatus.Completed,
                    Fee = 250.00m,
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-20)
                },
                new()
                {
                    PatientId = patients[2].Id,
                    DoctorId = sarahId,
                    DoctorName = "Dr. Sarah Jenkins, MD",
                    ConsultationDate = DateTime.UtcNow.AddDays(-7),
                    DurationMinutes = 30,
                    Diagnosis = "Acute Bronchial Spasm with Cough",
                    Symptoms = "Shortness of breath with wheezing after exercise",
                    Notes = "Lungs show mild bilateral expiratory wheezes. O2 sat 98%.",
                    TreatmentPlan = "Use rescue inhaler before intense workouts. Keep peak flow meter log.",
                    Prescription = "Albuterol sulfate HFA 90mcg inhaler (2 puffs Q4H PRN).",
                    Status = ConsultationStatus.Completed,
                    Fee = 175.00m,
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-7)
                },
                new()
                {
                    PatientId = patients[3].Id,
                    DoctorId = jamesId,
                    DoctorName = "Dr. James Wilson, Orthopedic Specialist",
                    ConsultationDate = DateTime.UtcNow.Date.AddDays(3).AddHours(11),
                    DurationMinutes = 30,
                    Diagnosis = "Right Knee Post-Surgical Follow-up",
                    Symptoms = "Mild stiffness during stairs",
                    Notes = "Scheduled review of knee range of motion and joint stability.",
                    TreatmentPlan = "Review physical therapy progression.",
                    Status = ConsultationStatus.Scheduled,
                    Fee = 220.00m,
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-2)
                }
            };

            await context.Consultations.AddRangeAsync(consultations);
            await context.SaveChangesAsync();

            logger.LogInformation("Database seeded with {DoctorCount} doctors, {PatientCount} patients, and {ConsultationCount} consultations.",
                docList.Count, patients.Count, consultations.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
        }
    }
}
