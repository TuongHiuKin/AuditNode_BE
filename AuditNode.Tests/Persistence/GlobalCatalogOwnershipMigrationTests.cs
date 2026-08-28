using AuditNode.Infrastructure.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Text.RegularExpressions;
using Xunit;

namespace AuditNode.Tests.Persistence;

public sealed class GlobalCatalogOwnershipMigrationTests
{
    [Fact]
    public void Up_ShouldBeAdditiveAndIntroduceEveryTransitionalOwnerColumn()
    {
        var operations = new InspectableMigration().GetUpOperations();

        operations.OfType<DropTableOperation>().Should().BeEmpty();
        operations.OfType<DropColumnOperation>().Should().BeEmpty();

        operations.OfType<AddColumnOperation>()
            .Where(operation => operation.Name == "owner_user_id")
            .Select(operation => operation.Table)
            .Should().BeEquivalentTo(
            [
                "datacenters",
                "servers",
                "applications",
                "port_mappings",
                "labels",
                "server_labels",
                "application_labels",
                "app_dependencies",
                "topology_nodes",
                "topology_edges"
            ]);

        operations.OfType<AddColumnOperation>()
            .Where(operation => operation.Name == "owner_user_id")
            .Should().OnlyContain(operation => operation.IsNullable,
                "legacy Workspace rows have no trustworthy owner until the approved reset");
    }

    [Fact]
    public void Up_ShouldCreateUserOrAnonymousViewerGrantWithoutInviteSchema()
    {
        var operations = new InspectableMigration().GetUpOperations();
        var grantTable = operations.OfType<CreateTableOperation>()
            .Single(operation => operation.Name == "label_grants");

        grantTable.Columns.Select(column => column.Name).Should().Contain(
            "owner_user_id", "label_id", "grantee_user_id", "permission", "token_hash",
            "expires_at", "revoked_at", "version", "created_by_user_id");
        grantTable.CheckConstraints.Select(constraint => constraint.Name).Should().Contain(
            "ck_label_grants_subject",
            "ck_label_grants_permission",
            "ck_label_grants_anonymous_viewer");
        grantTable.CheckConstraints.Single(constraint =>
                constraint.Name == "ck_label_grants_anonymous_viewer")
            .Sql.Should().Be("token_hash IS NULL OR permission = 'viewer'");

        operations.OfType<CreateTableOperation>()
            .Should().NotContain(operation => operation.Name.Contains("invite", StringComparison.OrdinalIgnoreCase));
        operations.OfType<CreateTableOperation>()
            .Should().ContainSingle(operation => operation.Name == "owner_catalog_states");
    }

    [Fact]
    public void OwnerViewArtifact_ShouldProjectOwnerWithoutDroppingWorkspaceCompatibility()
    {
        var assembly = typeof(GlobalCatalogOwnershipFoundation).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("20260828_global_catalog_owner_views.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        var topologyColumns = ExtractProjection(sql, "v_topology_map");
        topologyColumns.Should().Equal(
            "server.workspace_id",
            "server.id AS server_id",
            "server.hostname AS server_hostname",
            "server.ip_address AS server_ip",
            "application.id AS app_id",
            "application.app_name",
            "application.app_code",
            "mapping.port_number",
            "mapping.protocol",
            "server.environment",
            "server.datacenter_id",
            "server.owner_user_id AS owner_user_id");

        var dependencyColumns = ExtractProjection(sql, "v_dependency_graph");
        dependencyColumns.Should().Equal(
            "dependency.workspace_id",
            "source_application.id AS source_app_id",
            "source_application.app_name AS source_app_name",
            "source_application.app_code AS source_app_code",
            "destination_application.id AS dest_app_id",
            "destination_application.app_name AS dest_app_name",
            "destination_application.app_code AS dest_app_code",
            "destination_port.port_number AS dest_port_number",
            "dependency.connection_type",
            "destination_server.hostname AS dest_server_hostname",
            "destination_server.environment",
            "destination_server.datacenter_id",
            "dependency.owner_user_id AS owner_user_id");

        sql.Should().Contain("IS NOT DISTINCT FROM");
    }

    [Fact]
    public void HardeningMigration_ShouldEnforceOwnerConsistencyAndExpiringAnonymousTokens()
    {
        var operations = new InspectableHardeningMigration().GetUpOperations();

        operations.OfType<AddCheckConstraintOperation>().Should().ContainSingle(operation =>
            operation.Table == "label_grants" &&
            operation.Name == "ck_label_grants_token_expiry" &&
            operation.Sql == "token_hash IS NULL OR expires_at IS NOT NULL");

        var sql = string.Join('\n', operations.OfType<SqlOperation>().Select(operation => operation.Sql));
        sql.Should().Contain("CREATE UNIQUE INDEX \"UX_labels_owner_user_id_id\"");
        sql.Should().Contain("FOREIGN KEY (\"owner_user_id\", \"label_id\")");
        sql.Should().Contain("REFERENCES \"labels\" (\"owner_user_id\", \"id\")");
        sql.Should().Contain("FOREIGN KEY (\"owner_user_id\", \"server_id\")");
        sql.Should().Contain("REFERENCES \"servers\" (\"owner_user_id\", \"id\")");
        sql.Should().Contain("FOREIGN KEY (\"owner_user_id\", \"application_id\")");
        sql.Should().Contain("REFERENCES \"applications\" (\"owner_user_id\", \"id\")");
        sql.Should().Contain("NOT VALID");
        sql.Should().Contain("VALIDATE CONSTRAINT");

        var ownerForeignKeys = ExtractOwnerForeignKeys(sql);
        ownerForeignKeys.Should().BeEquivalentTo(
        new ForeignKeySpec[]
        {
            new("FK_label_grants_labels_owner_label", "label_grants", "owner_user_id,label_id", "labels", "owner_user_id,id"),
            new("FK_server_labels_servers_owner_server", "server_labels", "owner_user_id,server_id", "servers", "owner_user_id,id"),
            new("FK_server_labels_labels_owner_label", "server_labels", "owner_user_id,label_id", "labels", "owner_user_id,id"),
            new("FK_application_labels_applications_owner_app", "application_labels", "owner_user_id,application_id", "applications", "owner_user_id,id"),
            new("FK_application_labels_labels_owner_label", "application_labels", "owner_user_id,label_id", "labels", "owner_user_id,id"),
            new("FK_servers_datacenters_owner_datacenter", "servers", "owner_user_id,datacenter_id", "datacenters", "owner_user_id,id"),
            new("FK_port_mappings_servers_owner_server", "port_mappings", "owner_user_id,server_id", "servers", "owner_user_id,id"),
            new("FK_port_mappings_applications_owner_app", "port_mappings", "owner_user_id,app_id", "applications", "owner_user_id,id"),
            new("FK_app_dependencies_applications_owner_source", "app_dependencies", "owner_user_id,source_app_id", "applications", "owner_user_id,id"),
            new("FK_app_dependencies_applications_owner_dest", "app_dependencies", "owner_user_id,dest_app_id", "applications", "owner_user_id,id"),
            new("FK_app_dependencies_port_mappings_owner_dest_port", "app_dependencies", "owner_user_id,dest_port_id", "port_mappings", "owner_user_id,id"),
            new("FK_topology_nodes_topology_nodes_owner_parent", "topology_nodes", "owner_user_id,parent_node_id", "topology_nodes", "owner_user_id,id"),
            new("FK_topology_edges_topology_nodes_owner_source", "topology_edges", "owner_user_id,source_node_id", "topology_nodes", "owner_user_id,id"),
            new("FK_topology_edges_topology_nodes_owner_target", "topology_edges", "owner_user_id,target_node_id", "topology_nodes", "owner_user_id,id")
        });
        Regex.Matches(sql, @"ON DELETE (?:CASCADE|RESTRICT) NOT VALID;")
            .Should().HaveCount(ownerForeignKeys.Length);
        Regex.Matches(sql, "VALIDATE CONSTRAINT \\\"(?<name>FK_[^\\\"]+)\\\"")
            .Select(match => match.Groups["name"].Value)
            .Should().BeEquivalentTo(ownerForeignKeys.Select(foreignKey => foreignKey.Name));

        Regex.Matches(
                sql,
                "CREATE UNIQUE INDEX \\\"(?<name>UX_[^\\\"]+)\\\"\\s+ON \\\"(?<table>[^\\\"]+)\\\" \\(" +
                "\\\"owner_user_id\\\", \\\"id\\\"\\)",
                RegexOptions.Singleline)
            .Select(match => (match.Groups["name"].Value, match.Groups["table"].Value))
            .Should().BeEquivalentTo(
                new (string Name, string Table)[]
                {
                    ("UX_labels_owner_user_id_id", "labels"),
                    ("UX_servers_owner_user_id_id", "servers"),
                    ("UX_applications_owner_user_id_id", "applications"),
                    ("UX_datacenters_owner_user_id_id", "datacenters"),
                    ("UX_port_mappings_owner_user_id_id", "port_mappings"),
                    ("UX_topology_nodes_owner_user_id_id", "topology_nodes")
                });

        var downSql = string.Join('\n', new InspectableHardeningMigration().GetDownOperations()
            .OfType<SqlOperation>().Select(operation => operation.Sql));
        downSql.LastIndexOf("DROP CONSTRAINT", StringComparison.Ordinal)
            .Should().BeLessThan(downSql.IndexOf("DROP INDEX", StringComparison.Ordinal));
        Regex.Matches(downSql, "DROP CONSTRAINT \\\"(?<name>FK_[^\\\"]+)\\\"")
            .Select(match => match.Groups["name"].Value)
            .Should().BeEquivalentTo(ownerForeignKeys.Select(foreignKey => foreignKey.Name));
    }

    [Fact]
    public void FoundationDown_ShouldRestoreWorkspaceViewsBeforeDroppingOwnerColumns()
    {
        var operations = new InspectableMigration().GetDownOperations();
        var firstDropColumnIndex = operations
            .Select((operation, index) => (operation, index))
            .First(item => item.operation is DropColumnOperation).index;
        var restoreIndex = operations
            .Select((operation, index) => (operation, index))
            .First(item => item.operation is SqlOperation sql &&
                sql.Sql.Contains("CREATE OR REPLACE VIEW v_topology_map", StringComparison.Ordinal)).index;

        restoreIndex.Should().BeLessThan(firstDropColumnIndex);
        var restoreSql = ((SqlOperation)operations[restoreIndex]).Sql;
        ExtractProjection(restoreSql, "v_topology_map").Should().NotContain(column =>
            column.Contains("owner_user_id", StringComparison.Ordinal));
        ExtractProjection(restoreSql, "v_dependency_graph").Should().NotContain(column =>
            column.Contains("owner_user_id", StringComparison.Ordinal));
    }

    [Fact]
    public void OwnerViewRollbackArtifact_ShouldDropRestrictivelyAndRestoreLegacySignatures()
    {
        var assembly = typeof(GlobalCatalogOwnershipFoundation).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("20260828_global_catalog_owner_views_rollback.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        sql.Should().Contain("DROP VIEW IF EXISTS v_topology_map;");
        sql.Should().Contain("DROP VIEW IF EXISTS v_dependency_graph;");
        sql.Should().NotContain("CASCADE");
        ExtractProjection(sql, "v_topology_map").Should().HaveCount(11).And.NotContain(column =>
            column.Contains("owner_user_id", StringComparison.Ordinal));
        ExtractProjection(sql, "v_dependency_graph").Should().HaveCount(12).And.NotContain(column =>
            column.Contains("owner_user_id", StringComparison.Ordinal));
    }

    private sealed class InspectableMigration : GlobalCatalogOwnershipFoundation
    {
        public IReadOnlyList<MigrationOperation> GetUpOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Up(builder);
            return builder.Operations;
        }

        public IReadOnlyList<MigrationOperation> GetDownOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Down(builder);
            return builder.Operations;
        }
    }

    private sealed class InspectableHardeningMigration : GlobalCatalogOwnershipHardening
    {
        public IReadOnlyList<MigrationOperation> GetUpOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Up(builder);
            return builder.Operations;
        }

        public IReadOnlyList<MigrationOperation> GetDownOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            Down(builder);
            return builder.Operations;
        }
    }

    private static string[] ExtractProjection(string sql, string viewName)
    {
        var match = Regex.Match(
            sql,
            $@"CREATE\s+OR\s+REPLACE\s+VIEW\s+{Regex.Escape(viewName)}\s+AS\s+SELECT\s+(?<columns>.*?)\s+FROM\s+",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        match.Success.Should().BeTrue($"{viewName} must be an executable CREATE OR REPLACE VIEW statement");

        return match.Groups["columns"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(column => Regex.Replace(column, @"\s+", " ").Trim())
            .ToArray();
    }

    private static ForeignKeySpec[] ExtractOwnerForeignKeys(string sql)
    {
        return Regex.Matches(
                sql,
                "ALTER TABLE \\\"(?<table>[^\\\"]+)\\\"\\s+" +
                "ADD CONSTRAINT \\\"(?<name>FK_[^\\\"]+)\\\"\\s+" +
                "FOREIGN KEY \\((?<columns>[^)]+)\\)\\s+" +
                "REFERENCES \\\"(?<principal>[^\\\"]+)\\\" \\((?<principalColumns>[^)]+)\\)",
                RegexOptions.Singleline)
            .Select(match => new ForeignKeySpec(
                match.Groups["name"].Value,
                match.Groups["table"].Value,
                NormalizeColumns(match.Groups["columns"].Value),
                match.Groups["principal"].Value,
                NormalizeColumns(match.Groups["principalColumns"].Value)))
            .ToArray();
    }

    private static string NormalizeColumns(string columns) =>
        string.Join(',', columns.Split(',', StringSplitOptions.TrimEntries)
            .Select(column => column.Trim('"')));

    private sealed record ForeignKeySpec(
        string Name,
        string Table,
        string Columns,
        string PrincipalTable,
        string PrincipalColumns);
}
