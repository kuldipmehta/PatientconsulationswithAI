using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatientManagement.Core.Entities;

namespace PatientManagement.Infrastructure.Data.Configurations;

public class PatientDocumentConfigurations :
    IEntityTypeConfiguration<PatientDocument>,
    IEntityTypeConfiguration<PatientDocumentChunk>
{
    public void Configure(EntityTypeBuilder<PatientDocument> builder)
    {
        builder.ToTable("PatientDocuments");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.FileName).IsRequired().HasMaxLength(260);
        builder.Property(d => d.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(d => d.DocumentType).IsRequired().HasMaxLength(50);
        builder.Property(d => d.Status).IsRequired().HasMaxLength(30);
        builder.Property(d => d.BlobName).HasMaxLength(300);
        builder.Property(d => d.Summary).HasMaxLength(1000);

        builder.HasQueryFilter(d => !d.IsDeleted);
        builder.HasIndex(d => d.PatientId);
        builder.HasIndex(d => d.IsDeleted);

        builder.HasOne(d => d.Patient)
            .WithMany(p => p.Documents)
            .HasForeignKey(d => d.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Consultation)
            .WithMany()
            .HasForeignKey(d => d.ConsultationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(d => d.Chunks)
            .WithOne(c => c.PatientDocument)
            .HasForeignKey(c => c.PatientDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<PatientDocumentChunk> builder)
    {
        builder.ToTable("PatientDocumentChunks");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.SourceType).IsRequired().HasMaxLength(30);
        builder.Property(c => c.SourceLabel).IsRequired().HasMaxLength(250);
        builder.Property(c => c.TextContent).IsRequired();
        builder.Property(c => c.EmbeddingVectorJson).IsRequired();

        builder.HasIndex(c => new { c.PatientId, c.SourceType });
        builder.HasIndex(c => c.PatientDocumentId);
        builder.HasIndex(c => c.ConsultationId);

        builder.HasQueryFilter(c => !c.Patient.IsDeleted && (c.PatientDocument == null || !c.PatientDocument.IsDeleted));

        builder.HasOne(c => c.Patient)
            .WithMany(p => p.DocumentChunks)
            .HasForeignKey(c => c.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Consultation)
            .WithMany()
            .HasForeignKey(c => c.ConsultationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
