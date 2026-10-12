using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepResearchAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDoiToSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Doi",
                table: "Sources",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Doi",
                table: "Sources");
        }
    }
}
