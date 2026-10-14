using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DockiUp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ImageVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentVersion",
                table: "ImageUpdates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LatestVersion",
                table: "ImageUpdates",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentVersion",
                table: "ImageUpdates");

            migrationBuilder.DropColumn(
                name: "LatestVersion",
                table: "ImageUpdates");
        }
    }
}
