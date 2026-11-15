using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace REA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGitRepositories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "git_repositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    RemoteUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    DefaultBranch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Account = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_git_repositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_git_repositories_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_git_repositories_ProjectId_LocalPath",
                table: "git_repositories",
                columns: new[] { "ProjectId", "LocalPath" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "git_repositories");
        }
    }
}
