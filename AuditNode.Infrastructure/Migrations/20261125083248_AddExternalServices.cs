using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditNode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "dest_port_id",
                table: "app_dependencies",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "dest_app_id",
                table: "app_dependencies",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "external_service_id",
                table: "app_dependencies",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "external_services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    protocol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    port = table.Column<int>(type: "integer", nullable: true),
                    environment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_services", x => x.id);
                    table.UniqueConstraint("AK_external_services_owner_user_id_id", x => new { x.owner_user_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_owner_user_id_external_service_id",
                table: "app_dependencies",
                columns: new[] { "owner_user_id", "external_service_id" });

            migrationBuilder.CreateIndex(
                name: "IX_app_dependencies_owner_user_id_source_app_id_external_servi~",
                table: "app_dependencies",
                columns: new[] { "owner_user_id", "source_app_id", "external_service_id" },
                unique: true,
                filter: "external_service_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_app_dependencies_single_destination",
                table: "app_dependencies",
                sql: "(dest_app_id IS NOT NULL AND dest_port_id IS NOT NULL AND external_service_id IS NULL) OR (dest_app_id IS NULL AND dest_port_id IS NULL AND external_service_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_external_services_owner_user_id_name",
                table: "external_services",
                columns: new[] { "owner_user_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_app_dependencies_external_services_owner_user_id_external_s~",
                table: "app_dependencies",
                columns: new[] { "owner_user_id", "external_service_id" },
                principalTable: "external_services",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_app_dependencies_external_services_owner_user_id_external_s~",
                table: "app_dependencies");

            migrationBuilder.DropTable(
                name: "external_services");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_owner_user_id_external_service_id",
                table: "app_dependencies");

            migrationBuilder.DropIndex(
                name: "IX_app_dependencies_owner_user_id_source_app_id_external_servi~",
                table: "app_dependencies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_app_dependencies_single_destination",
                table: "app_dependencies");

            migrationBuilder.DropColumn(
                name: "external_service_id",
                table: "app_dependencies");

            migrationBuilder.AlterColumn<Guid>(
                name: "dest_port_id",
                table: "app_dependencies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "dest_app_id",
                table: "app_dependencies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
