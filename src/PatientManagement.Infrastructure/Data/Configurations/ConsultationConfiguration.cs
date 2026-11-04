using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatientManagement.Core.Entities;

namespace PatientManagement.Infrastructure.Data.Configurations;

public class ConsultationConfiguration : IEntityTypeConfiguration<Consultation>
{
    public void Configure(EntityTypeBuilder<Consultation> builder)
    {
        builder.ToTable("Consultations");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.DoctorName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.Diagnosis)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(c => c.Symptoms)
            .HasMaxLength(500);

        builder.Property(c => c.Notes)
            .HasMaxLength(2000);

        builder.Property(c => c.TreatmentPlan)
            .HasMaxLength(1000);

        builder.Property(c => c.Prescription)
            .HasMaxLength(1000);

        builder.Property(c => c.Status)
            .IsRequired();

        builder.Property(c => c.Fee)
            .HasPrecision(10, 2);

        builder.Property(c => c.ConsultationDate)
            .IsRequired();

        builder.Property(c => c.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(c => c.PatientId);
        builder.HasIndex(c => c.ConsultationDate);
    }
}
