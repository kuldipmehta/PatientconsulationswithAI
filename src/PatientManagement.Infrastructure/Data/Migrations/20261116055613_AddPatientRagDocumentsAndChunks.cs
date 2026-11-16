using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatientManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientRagDocumentsAndChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PatientDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PatientId = table.Column<int>(type: "int", nullable: false),
                    ConsultationId = table.Column<int>(type: "int", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    BlobName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TotalChunks = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientDocuments_Consultations_ConsultationId",
                        column: x => x.ConsultationId,
                        principalTable: "Consultations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PatientDocuments_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientDocumentChunks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PatientDocumentId = table.Column<int>(type: "int", nullable: true),
                    PatientId = table.Column<int>(type: "int", nullable: false),
                    ConsultationId = table.Column<int>(type: "int", nullable: true),
                    ChunkIndex = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceLabel = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    TextContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmbeddingVectorJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmbeddingDimensions = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientDocumentChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientDocumentChunks_Consultations_ConsultationId",
                        column: x => x.ConsultationId,
                        principalTable: "Consultations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PatientDocumentChunks_PatientDocuments_PatientDocumentId",
                        column: x => x.PatientDocumentId,
                        principalTable: "PatientDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PatientDocumentChunks_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientDocumentChunks_ConsultationId",
                table: "PatientDocumentChunks",
                column: "ConsultationId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDocumentChunks_PatientDocumentId",
                table: "PatientDocumentChunks",
                column: "PatientDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDocumentChunks_PatientId_SourceType",
                table: "PatientDocumentChunks",
                columns: new[] { "PatientId", "SourceType" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientDocuments_ConsultationId",
                table: "PatientDocuments",
                column: "ConsultationId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDocuments_IsDeleted",
                table: "PatientDocuments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDocuments_PatientId",
                table: "PatientDocuments",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatientDocumentChunks");

            migrationBuilder.DropTable(
                name: "PatientDocuments");
        }
    }
}
