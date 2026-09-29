using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DockiUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentsAndWebhookSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // WebhookUrl was a user-typed URL DockiUp never used; it is not a branch, so drop it.
            migrationBuilder.DropColumn(
                name: "WebhookUrl",
                table: "ProjectInfo");

            migrationBuilder.AddColumn<string>(
                name: "Branch",
                table: "ProjectInfo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookSecret",
                table: "ProjectInfo",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Existing projects need a real secret (an empty HMAC key is forgeable by anyone).
            // gen_random_uuid() draws from a CSPRNG; two of them give 244 random bits.
            migrationBuilder.Sql("""UPDATE "ProjectInfo" SET "WebhookSecret" = replace(gen_random_uuid()::text || gen_random_uuid()::text, '-', '')""");

            migrationBuilder.CreateTable(
                name: "Deployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Trigger = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ActorName = table.Column<string>(type: "text", nullable: true),
                    CommitBefore = table.Column<string>(type: "text", nullable: true),
                    CommitAfter = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Log = table.Column<string>(type: "text", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deployments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ProjectId_CreatedAt",
                table: "Deployments",
                columns: new[] { "ProjectId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Deployments");

            migrationBuilder.DropColumn(
                name: "WebhookSecret",
                table: "ProjectInfo");

            migrationBuilder.DropColumn(
                name: "Branch",
                table: "ProjectInfo");

            migrationBuilder.AddColumn<string>(
                name: "WebhookUrl",
                table: "ProjectInfo",
                type: "text",
                nullable: true);
        }
    }
}
