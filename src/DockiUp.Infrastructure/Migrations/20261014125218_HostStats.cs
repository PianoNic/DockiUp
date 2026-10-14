using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DockiUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HostStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostStatSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CpuPercent = table.Column<double>(type: "double precision", nullable: false),
                    MemoryUsed = table.Column<long>(type: "bigint", nullable: false),
                    MemoryCache = table.Column<long>(type: "bigint", nullable: false),
                    MemoryTotal = table.Column<long>(type: "bigint", nullable: false),
                    NetworkRxRate = table.Column<double>(type: "double precision", nullable: true),
                    NetworkTxRate = table.Column<double>(type: "double precision", nullable: true),
                    DiskReadRate = table.Column<double>(type: "double precision", nullable: true),
                    DiskWriteRate = table.Column<double>(type: "double precision", nullable: true),
                    GpuUtilization = table.Column<double>(type: "double precision", nullable: true),
                    GpuMemoryUsed = table.Column<long>(type: "bigint", nullable: true),
                    GpuMemoryTotal = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostStatSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeedTestResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DownloadMbps = table.Column<double>(type: "double precision", nullable: true),
                    UploadMbps = table.Column<double>(type: "double precision", nullable: true),
                    PingMs = table.Column<double>(type: "double precision", nullable: true),
                    JitterMs = table.Column<double>(type: "double precision", nullable: true),
                    Server = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeedTestResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostStatSamples_NodeId_Timestamp",
                table: "HostStatSamples",
                columns: new[] { "NodeId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeedTestResults_NodeId_At",
                table: "SpeedTestResults",
                columns: new[] { "NodeId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostStatSamples");

            migrationBuilder.DropTable(
                name: "SpeedTestResults");
        }
    }
}
