using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatientManagement.Core.Entities;

namespace PatientManagement.Infrastructure.Data.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.FirstName)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.LastName)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.Email)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(p => p.Email);

        builder.Property(p => p.PhoneNumber)
            .HasMaxLength(20);

        builder.Property(p => p.Gender)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(p => p.Address)
            .HasMaxLength(200);

        builder.Property(p => p.MedicalNotes)
            .HasMaxLength(1000);

        builder.Property(p => p.ProfilePictureUrl)
            .HasMaxLength(1000);

        builder.Property(p => p.ProfilePictureBlobName)
            .HasMaxLength(250);

        builder.Property(p => p.CreatedAtUtc)
            .IsRequired();

        // 1-to-many relationship with Consultations
        builder.HasMany(p => p.Consultations)
            .WithOne(c => c.Patient)
            .HasForeignKey(c => c.PatientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
