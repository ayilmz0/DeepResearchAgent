using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepResearchAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCrawlerStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "CrawledAt",
                table: "Sources",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<bool>(
                name: "CrawlSucceeded",
                table: "Sources",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CrawlSucceeded",
                table: "Sources");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CrawledAt",
                table: "Sources",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }
    }
}
