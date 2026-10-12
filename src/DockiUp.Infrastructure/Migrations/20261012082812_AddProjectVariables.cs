using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DockiUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectVariables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Group",
                table: "ProjectSecrets",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectVariables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Group = table.Column<string>(type: "text", nullable: true),
                    IsSecret = table.Column<bool>(type: "boolean", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectVariables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectVariables_ProjectInfo_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ProjectInfo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVariables_ProjectId_Name",
                table: "ProjectVariables",
                columns: new[] { "ProjectId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectVariables");

            migrationBuilder.DropColumn(
                name: "Group",
                table: "ProjectSecrets");
        }
    }
}
