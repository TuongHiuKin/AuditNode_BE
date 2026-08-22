using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationLabelsAndDeploymentContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_labels_workspace_id_id",
                table: "labels",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.CreateTable(
                name: "application_labels",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_labels", x => new { x.workspace_id, x.application_id, x.label_id });
                    table.ForeignKey(
                        name: "FK_application_labels_applications_workspace_id_application_id",
                        columns: x => new { x.workspace_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_application_labels_labels_workspace_id_label_id",
                        columns: x => new { x.workspace_id, x.label_id },
                        principalTable: "labels",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_application_labels_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_labels_workspace_id_label_id",
                table: "application_labels",
                columns: new[] { "workspace_id", "label_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_labels");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_labels_workspace_id_id",
                table: "labels");
        }
    }
}
