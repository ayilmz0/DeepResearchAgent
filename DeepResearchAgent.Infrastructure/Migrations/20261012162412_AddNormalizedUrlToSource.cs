using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepResearchAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedUrlToSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sources_ResearchId",
                table: "Sources");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedUrl",
                table: "Sources",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Sources_ResearchId_NormalizedUrl",
                table: "Sources",
                columns: new[] { "ResearchId", "NormalizedUrl" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sources_ResearchId_NormalizedUrl",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "NormalizedUrl",
                table: "Sources");

            migrationBuilder.CreateIndex(
                name: "IX_Sources_ResearchId",
                table: "Sources",
                column: "ResearchId");
        }
    }
}
