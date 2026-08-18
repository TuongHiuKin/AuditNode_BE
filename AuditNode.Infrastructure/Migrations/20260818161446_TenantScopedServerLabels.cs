using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantScopedServerLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_server_labels_labels_LabelsId",
                table: "server_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_server_labels_servers_ServersId",
                table: "server_labels");

            migrationBuilder.DropPrimaryKey(
                name: "PK_server_labels",
                table: "server_labels");

            migrationBuilder.DropIndex(
                name: "IX_server_labels_ServersId",
                table: "server_labels");

            migrationBuilder.RenameColumn(
                name: "ServersId",
                table: "server_labels",
                newName: "server_id");

            migrationBuilder.RenameColumn(
                name: "LabelsId",
                table: "server_labels",
                newName: "label_id");

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "server_labels",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM server_labels AS link
                        LEFT JOIN servers AS server ON server.id = link.server_id
                        LEFT JOIN labels AS label ON label.id = link.label_id
                        WHERE server.id IS NULL OR label.id IS NULL
                    ) THEN
                        RAISE EXCEPTION 'Server-label backfill found an orphaned server or label reference.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM server_labels AS link
                        JOIN servers AS server ON server.id = link.server_id
                        JOIN labels AS label ON label.id = link.label_id
                        WHERE server.workspace_id <> label.workspace_id
                    ) THEN
                        RAISE EXCEPTION 'Server-label backfill found a cross-workspace association; resolve it explicitly before continuing.';
                    END IF;

                    UPDATE server_labels AS link
                    SET workspace_id = server.workspace_id
                    FROM servers AS server, labels AS label
                    WHERE server.id = link.server_id
                      AND label.id = link.label_id
                      AND server.workspace_id = label.workspace_id;

                    IF EXISTS (SELECT 1 FROM server_labels WHERE workspace_id IS NULL) THEN
                        RAISE EXCEPTION 'Server-label workspace backfill is incomplete.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "workspace_id",
                table: "server_labels",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_server_labels",
                table: "server_labels",
                columns: new[] { "workspace_id", "server_id", "label_id" });

            migrationBuilder.CreateIndex(
                name: "IX_server_labels_workspace_id_label_id",
                table: "server_labels",
                columns: new[] { "workspace_id", "label_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_server_labels_labels_workspace_id_label_id",
                table: "server_labels",
                columns: new[] { "workspace_id", "label_id" },
                principalTable: "labels",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_server_labels_servers_workspace_id_server_id",
                table: "server_labels",
                columns: new[] { "workspace_id", "server_id" },
                principalTable: "servers",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_server_labels_workspaces_workspace_id",
                table: "server_labels",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_server_labels_labels_workspace_id_label_id",
                table: "server_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_server_labels_servers_workspace_id_server_id",
                table: "server_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_server_labels_workspaces_workspace_id",
                table: "server_labels");

            migrationBuilder.DropPrimaryKey(
                name: "PK_server_labels",
                table: "server_labels");

            migrationBuilder.DropIndex(
                name: "IX_server_labels_workspace_id_label_id",
                table: "server_labels");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "server_labels");

            migrationBuilder.RenameColumn(
                name: "label_id",
                table: "server_labels",
                newName: "LabelsId");

            migrationBuilder.RenameColumn(
                name: "server_id",
                table: "server_labels",
                newName: "ServersId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_server_labels",
                table: "server_labels",
                columns: new[] { "LabelsId", "ServersId" });

            migrationBuilder.CreateIndex(
                name: "IX_server_labels_ServersId",
                table: "server_labels",
                column: "ServersId");

            migrationBuilder.AddForeignKey(
                name: "FK_server_labels_labels_LabelsId",
                table: "server_labels",
                column: "LabelsId",
                principalTable: "labels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_server_labels_servers_ServersId",
                table: "server_labels",
                column: "ServersId",
                principalTable: "servers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
