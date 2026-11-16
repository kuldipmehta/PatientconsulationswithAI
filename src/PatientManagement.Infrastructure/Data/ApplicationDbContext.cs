using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    private readonly ICurrentUserService? _currentUserService;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService? currentUserService = null)
        : base(options)
    {
        _currentUserService = currentUserService;
    }

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Consultation> Consultations => Set<Consultation>();
    public DbSet<EmailAuditLog> EmailAuditLogs => Set<EmailAuditLog>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<PasswordSetupToken> PasswordSetupTokens => Set<PasswordSetupToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AiInteractionLog> AiInteractionLogs => Set<AiInteractionLog>();
    public DbSet<PendingAiAction> PendingAiActions => Set<PendingAiAction>();
    public DbSet<AiChatSession> AiChatSessions => Set<AiChatSession>();
    public DbSet<AiChatSessionMessage> AiChatSessionMessages => Set<AiChatSessionMessage>();
    public DbSet<PatientDocument> PatientDocuments => Set<PatientDocument>();
    public DbSet<PatientDocumentChunk> PatientDocumentChunks => Set<PatientDocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var pendingAudits = CaptureAuditEntries();

        var result = await base.SaveChangesAsync(cancellationToken);

        if (pendingAudits.Count > 0)
        {
            foreach (var item in pendingAudits)
            {
                item.Log.EntityId = item.GetEntityId();
                AuditLogs.Add(item.Log);
            }

            await base.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    private List<PendingAuditRecord> CaptureAuditEntries()
    {
        ChangeTracker.DetectChanges();
        var records = new List<PendingAuditRecord>();

        var userId = _currentUserService?.UserId;
        var userEmail = _currentUserService?.Email ?? "system";
        var userRole = _currentUserService?.Role?.ToString() ?? "System";

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog or AiInteractionLog or EmailAuditLog or AiChatSessionMessage)
            {
                continue;
            }

            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is not (Patient or Consultation or Doctor or AppUser))
            {
                continue;
            }

            var action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Deleted => "HardDeleted",
                EntityState.Modified when IsSoftDeletedNow(entry) => "SoftDeleted",
                _ => "Updated"
            };

            var changes = BuildChangesDictionary(entry);
            var log = new AuditLog
            {
                UserId = userId,
                UserEmail = userEmail,
                UserRole = userRole,
                Action = action,
                EntityName = entry.Entity.GetType().Name,
                ChangesJson = changes.Count > 0 ? JsonSerializer.Serialize(changes) : null,
                TimestampUtc = DateTime.UtcNow
            };

            records.Add(new PendingAuditRecord(log, entry));
        }

        return records;
    }

    private static bool IsSoftDeletedNow(EntityEntry entry)
    {
        var isDeletedProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "IsDeleted");
        return isDeletedProp is { IsModified: true, CurrentValue: true };
    }

    private static Dictionary<string, object?> BuildChangesDictionary(EntityEntry entry)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PasswordHash", "RowVersion", "TokenHash", "ProfilePictureBlobName"
        };

        var dict = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties)
        {
            if (excluded.Contains(prop.Metadata.Name))
            {
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                dict[prop.Metadata.Name] = prop.CurrentValue;
            }
            else if (entry.State == EntityState.Modified && prop.IsModified)
            {
                dict[prop.Metadata.Name] = new { From = prop.OriginalValue, To = prop.CurrentValue };
            }
        }

        return dict;
    }

    private sealed class PendingAuditRecord(AuditLog log, EntityEntry entry)
    {
        public AuditLog Log { get; } = log;

        public string GetEntityId()
        {
            var idProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
            return idProp?.CurrentValue?.ToString() ?? string.Empty;
        }
    }
}
