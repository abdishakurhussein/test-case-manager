using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TestCaseManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class ManualRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManualRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TestCaseId = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Result = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManualRuns_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManualStepResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ManualRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalStepId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedResult = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualStepResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManualStepResults_ManualRuns_ManualRunId",
                        column: x => x.ManualRunId,
                        principalTable: "ManualRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManualRuns_TestCaseId",
                table: "ManualRuns",
                column: "TestCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualStepResults_ManualRunId",
                table: "ManualStepResults",
                column: "ManualRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManualStepResults");

            migrationBuilder.DropTable(
                name: "ManualRuns");
        }
    }
}
