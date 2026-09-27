using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TestCaseManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class FailureDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActualResult",
                table: "ManualStepResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanReplicate",
                table: "ManualStepResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OnlyUserAffected",
                table: "ManualStepResults",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualResult",
                table: "ManualStepResults");

            migrationBuilder.DropColumn(
                name: "CanReplicate",
                table: "ManualStepResults");

            migrationBuilder.DropColumn(
                name: "OnlyUserAffected",
                table: "ManualStepResults");
        }
    }
}
