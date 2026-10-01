using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DockiUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImageUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "ImageUpdateExcludedServices",
                table: "ProjectInfo",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'"); // existing projects: nothing excluded

            migrationBuilder.AddColumn<string>(
                name: "ImageUpdatePolicy",
                table: "ProjectInfo",
                type: "text",
                nullable: false,
                defaultValue: "Notify"); // existing projects get the default policy

            migrationBuilder.CreateTable(
                name: "ImageUpdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceName = table.Column<string>(type: "text", nullable: false),
                    Image = table.Column<string>(type: "text", nullable: false),
                    CurrentDigest = table.Column<string>(type: "text", nullable: true),
                    LatestDigest = table.Column<string>(type: "text", nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageUpdates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImageUpdates_ProjectId_ServiceName",
                table: "ImageUpdates",
                columns: new[] { "ProjectId", "ServiceName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImageUpdates");

            migrationBuilder.DropColumn(
                name: "ImageUpdateExcludedServices",
                table: "ProjectInfo");

            migrationBuilder.DropColumn(
                name: "ImageUpdatePolicy",
                table: "ProjectInfo");
        }
    }
}
