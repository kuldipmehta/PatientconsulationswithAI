using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatientManagement.Core.Entities;

namespace PatientManagement.Infrastructure.Data.Configurations;

public class AuditAndAiConfigurations :
    IEntityTypeConfiguration<AuditLog>,
    IEntityTypeConfiguration<AiInteractionLog>,
    IEntityTypeConfiguration<PendingAiAction>,
    IEntityTypeConfiguration<AiChatSession>,
    IEntityTypeConfiguration<AiChatSessionMessage>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.UserEmail).IsRequired().HasMaxLength(120);
        builder.Property(a => a.UserRole).IsRequired().HasMaxLength(40);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(40);
        builder.Property(a => a.EntityName).IsRequired().HasMaxLength(80);
        builder.Property(a => a.EntityId).IsRequired().HasMaxLength(80);
        builder.HasIndex(a => a.TimestampUtc);
    }

    public void Configure(EntityTypeBuilder<AiInteractionLog> builder)
    {
        builder.ToTable("AiInteractionLogs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.UserEmail).IsRequired().HasMaxLength(120);
        builder.Property(a => a.UserRole).IsRequired().HasMaxLength(40);
        builder.Property(a => a.Endpoint).IsRequired().HasMaxLength(120);
        builder.Property(a => a.ModelUsed).IsRequired().HasMaxLength(80);
        builder.Property(a => a.EstimatedCostUsd).HasPrecision(18, 8);
        builder.HasIndex(a => a.CreatedAtUtc);
    }

    public void Configure(EntityTypeBuilder<PendingAiAction> builder)
    {
        builder.ToTable("PendingAiActions");
        builder.HasKey(a => a.ActionId);
        builder.Property(a => a.ActionId).HasMaxLength(64);
        builder.Property(a => a.ActionType).IsRequired().HasMaxLength(64);
        builder.Property(a => a.Summary).IsRequired().HasMaxLength(500);
        builder.Property(a => a.Status).IsRequired().HasMaxLength(32);
        builder.HasIndex(a => new { a.UserId, a.Status });
    }

    public void Configure(EntityTypeBuilder<AiChatSession> builder)
    {
        builder.ToTable("AiChatSessions");
        builder.HasKey(s => s.SessionId);
        builder.Property(s => s.SessionId).HasMaxLength(64);
        builder.HasIndex(s => s.UserId);
        builder.HasMany(s => s.Messages)
            .WithOne(m => m.Session)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<AiChatSessionMessage> builder)
    {
        builder.ToTable("AiChatSessionMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Role).IsRequired().HasMaxLength(20);
        builder.HasIndex(m => m.SessionId);
    }
}
