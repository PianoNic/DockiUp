using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DockiUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Containers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContainerId = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GitUrl = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Path = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastGitPush = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    UpdateMethod = table.Column<string>(type: "text", nullable: true),
                    SetupStatus = table.Column<int>(type: "integer", nullable: false),
                    WebhookSecretId = table.Column<int>(type: "integer", nullable: true),
                    CheckIntervals = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Containers", x => x.Id);
                    table.CheckConstraint("CK_Container_ManualNoWebhookOrIntervals", "\"UpdateMethod\" != 'UpdateManually' OR (\"WebhookSecretId\" IS NULL AND \"CheckIntervals\" IS NULL)");
                    table.CheckConstraint("CK_Container_WebhookOrCheckIntervals", "(\"WebhookSecretId\" IS NULL OR \"CheckIntervals\" IS NULL) AND NOT (\"WebhookSecretId\" IS NOT NULL AND \"CheckIntervals\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProfilePicture = table.Column<byte[]>(type: "bytea", nullable: true),
                    UserSettings_Id = table.Column<int>(type: "integer", nullable: false),
                    UserSettings_PreferredColorScheme = table.Column<string>(type: "text", nullable: false),
                    UserRole = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookSecrets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Identifier = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Secret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContainerId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSecrets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookSecrets_Containers_ContainerId",
                        column: x => x.ContainerId,
                        principalTable: "Containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSecrets_ContainerId",
                table: "WebhookSecrets",
                column: "ContainerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSecrets_Identifier",
                table: "WebhookSecrets",
                column: "Identifier",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "WebhookSecrets");

            migrationBuilder.DropTable(
                name: "Containers");
        }
    }
}
