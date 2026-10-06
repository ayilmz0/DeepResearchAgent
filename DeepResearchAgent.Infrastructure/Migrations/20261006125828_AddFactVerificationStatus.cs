using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepResearchAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFactVerificationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VerificationStatus",
                table: "Facts",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "Facts");
        }
    }
}
