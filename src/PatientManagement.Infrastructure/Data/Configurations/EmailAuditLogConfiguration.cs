using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatientManagement.Core.Entities;

namespace PatientManagement.Infrastructure.Data.Configurations;

public class EmailAuditLogConfiguration : IEntityTypeConfiguration<EmailAuditLog>
{
    public void Configure(EntityTypeBuilder<EmailAuditLog> builder)
    {
        builder.ToTable("EmailAuditLogs");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.RecipientEmail)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Subject)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Body)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.SentAtUtc)
            .IsRequired();

        builder.HasIndex(e => e.RecipientEmail);
        builder.HasIndex(e => e.PatientId);
    }
}
