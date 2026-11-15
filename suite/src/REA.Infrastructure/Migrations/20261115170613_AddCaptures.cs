using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace REA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "captures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    StoredFile = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    FileFormat = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OriginalPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    InterfaceName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    InterfaceDescription = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CaptureFilter = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    EndedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DroppedPackets = table.Column<long>(type: "INTEGER", nullable: false),
                    AnalysisStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    AnalysisError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PacketCount = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    TcpStreamCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstPacketAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastPacketAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ProtocolSummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_captures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_captures_environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_captures_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_captures_EnvironmentId",
                table: "captures",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_captures_ProjectId_CreatedAt",
                table: "captures",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_captures_Status",
                table: "captures",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "captures");
        }
    }
}
