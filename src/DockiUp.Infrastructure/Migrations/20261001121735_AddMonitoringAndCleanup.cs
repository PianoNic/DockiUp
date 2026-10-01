using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DockiUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringAndCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CleanupSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Frequency = table.Column<string>(type: "text", nullable: false),
                    HourUtc = table.Column<int>(type: "integer", nullable: false),
                    DayOfWeekUtc = table.Column<int>(type: "integer", nullable: false),
                    PruneVolumes = table.Column<bool>(type: "boolean", nullable: false),
                    SavedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRunResult = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CleanupSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContainerStatSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContainerId = table.Column<string>(type: "text", nullable: false),
                    ContainerName = table.Column<string>(type: "text", nullable: false),
                    ProjectName = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CpuPercent = table.Column<double>(type: "double precision", nullable: false),
                    MemoryUsage = table.Column<long>(type: "bigint", nullable: false),
                    MemoryLimit = table.Column<long>(type: "bigint", nullable: false),
                    NetworkRx = table.Column<long>(type: "bigint", nullable: false),
                    NetworkTx = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContainerStatSamples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CleanupSchedules_NodeId",
                table: "CleanupSchedules",
                column: "NodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContainerStatSamples_ContainerName_Timestamp",
                table: "ContainerStatSamples",
                columns: new[] { "ContainerName", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ContainerStatSamples_Timestamp",
                table: "ContainerStatSamples",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CleanupSchedules");

            migrationBuilder.DropTable(
                name: "ContainerStatSamples");
        }
    }
}
