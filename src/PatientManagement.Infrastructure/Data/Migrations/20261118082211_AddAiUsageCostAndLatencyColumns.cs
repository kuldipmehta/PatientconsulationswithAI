using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatientManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageCostAndLatencyColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompletionTokens",
                table: "AiInteractionLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCostUsd",
                table: "AiInteractionLogs",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LatencyMs",
                table: "AiInteractionLogs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "PromptTokens",
                table: "AiInteractionLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalTokens",
                table: "AiInteractionLogs",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletionTokens",
                table: "AiInteractionLogs");

            migrationBuilder.DropColumn(
                name: "EstimatedCostUsd",
                table: "AiInteractionLogs");

            migrationBuilder.DropColumn(
                name: "LatencyMs",
                table: "AiInteractionLogs");

            migrationBuilder.DropColumn(
                name: "PromptTokens",
                table: "AiInteractionLogs");

            migrationBuilder.DropColumn(
                name: "TotalTokens",
                table: "AiInteractionLogs");
        }
    }
}
