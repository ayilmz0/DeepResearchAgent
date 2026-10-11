using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepResearchAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCitationSupportsFact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SupportsFact",
                table: "Citations",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SupportsFact",
                table: "Citations");
        }
    }
}
