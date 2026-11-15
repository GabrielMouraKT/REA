using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace REA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyzedFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analyzed_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    OriginalPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    StoredFile = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    FormatDescription = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Architecture = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    GhidraStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    GhidraError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    GhidraVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    GhidraResultFile = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    GhidraFunctionCount = table.Column<int>(type: "INTEGER", nullable: true),
                    GhidraStringCount = table.Column<int>(type: "INTEGER", nullable: true),
                    GhidraAnalyzedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analyzed_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_analyzed_files_environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_analyzed_files_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_analyzed_files_EnvironmentId",
                table: "analyzed_files",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_analyzed_files_GhidraStatus",
                table: "analyzed_files",
                column: "GhidraStatus");

            migrationBuilder.CreateIndex(
                name: "IX_analyzed_files_ProjectId_CreatedAt",
                table: "analyzed_files",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_analyzed_files_ProjectId_Sha256",
                table: "analyzed_files",
                columns: new[] { "ProjectId", "Sha256" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analyzed_files");
        }
    }
}
