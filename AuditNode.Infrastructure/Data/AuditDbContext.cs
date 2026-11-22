using AuditNode.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Infrastructure.Data;

public class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<Datacenter> Datacenters => Set<Datacenter>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<AppEntity> Applications => Set<AppEntity>();
    public DbSet<PortMapping> PortMappings => Set<PortMapping>();
    public DbSet<AppDependency> AppDependencies => Set<AppDependency>();
    public DbSet<TopologyView> TopologyViews => Set<TopologyView>();
    public DbSet<DependencyView> DependencyViews => Set<DependencyView>();
    public DbSet<TopologyNode> TopologyNodes => Set<TopologyNode>();
    public DbSet<TopologyEdge> TopologyEdges => Set<TopologyEdge>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<ServerLabel> ServerLabels => Set<ServerLabel>();
    public DbSet<ApplicationLabel> ApplicationLabels => Set<ApplicationLabel>();
    public DbSet<LabelGrant> LabelGrants => Set<LabelGrant>();
    public DbSet<OwnerCatalogState> OwnerCatalogStates => Set<OwnerCatalogState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Datacenter>(entity =>
        {
            entity.ToTable("datacenters");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Location).HasColumnName("location").IsRequired();
            entity.HasAlternateKey(e => new { e.OwnerUserId, e.Id });
            entity.HasIndex(e => e.OwnerUserId);
        });

        modelBuilder.Entity<Server>(entity =>
        {
            entity.ToTable("servers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.DatacenterId).HasColumnName("datacenter_id").IsRequired();
            entity.Property(e => e.IpAddress).HasColumnName("ip_address").IsRequired();
            entity.Property(e => e.Hostname).HasColumnName("hostname").IsRequired();
            entity.Property(e => e.OsType).HasColumnName("os_type").IsRequired();
            entity.Property(e => e.Environment).HasColumnName("environment").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.HasAlternateKey(e => new { e.OwnerUserId, e.Id });
            entity.HasIndex(e => new { e.OwnerUserId, e.IpAddress }).IsUnique();
            entity.HasOne(e => e.Datacenter).WithMany(e => e.Servers)
                .HasForeignKey(e => new { e.OwnerUserId, e.DatacenterId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Labels).WithMany(e => e.Servers).UsingEntity<ServerLabel>();
        });

        modelBuilder.Entity<Label>(entity =>
        {
            entity.ToTable("labels", table =>
            {
                table.HasCheckConstraint("ck_labels_kind", "kind IN ('owner', 'business')");
                table.HasCheckConstraint("ck_labels_owner_protected", "kind <> 'owner' OR is_protected");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Key).HasColumnName("key").IsRequired();
            entity.Property(e => e.Value).HasColumnName("value").IsRequired();
            entity.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(20).HasDefaultValue(LabelKinds.Business).IsRequired();
            entity.Property(e => e.IsProtected).HasColumnName("is_protected").HasDefaultValue(false).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasAlternateKey(e => new { e.OwnerUserId, e.Id });
            entity.HasIndex(e => new { e.OwnerUserId, e.Key, e.Value }).IsUnique();
            entity.HasIndex(e => e.OwnerUserId).IsUnique().HasFilter("kind = 'owner' AND owner_user_id IS NOT NULL");
            entity.HasIndex(e => new { e.OwnerUserId, e.Kind });
        });

        modelBuilder.Entity<ServerLabel>(entity =>
        {
            entity.ToTable("server_labels");
            entity.HasKey(e => new { e.ServerId, e.LabelId });
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.ServerId).HasColumnName("server_id");
            entity.Property(e => e.LabelId).HasColumnName("label_id");
            entity.HasOne(e => e.Server).WithMany(e => e.ServerLabels)
                .HasForeignKey(e => new { e.OwnerUserId, e.ServerId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Label).WithMany(e => e.ServerLabels)
                .HasForeignKey(e => new { e.OwnerUserId, e.LabelId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.OwnerUserId, e.LabelId });
        });

        modelBuilder.Entity<AppEntity>(entity =>
        {
            entity.ToTable("applications");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.AppCode).HasColumnName("app_code").IsRequired();
            entity.Property(e => e.AppName).HasColumnName("app_name").IsRequired();
            entity.Property(e => e.OwnerTeam).HasColumnName("owner_team").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Risk).HasColumnName("risk").IsRequired();
            entity.Property(e => e.Icon).HasColumnName("icon");
            entity.Property(e => e.TechStack).HasColumnName("tech_stack");
            entity.HasAlternateKey(e => new { e.OwnerUserId, e.Id });
            entity.HasIndex(e => new { e.OwnerUserId, e.AppCode }).IsUnique();
        });

        modelBuilder.Entity<ApplicationLabel>(entity =>
        {
            entity.ToTable("application_labels");
            entity.HasKey(e => new { e.ApplicationId, e.LabelId });
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.ApplicationId).HasColumnName("application_id");
            entity.Property(e => e.LabelId).HasColumnName("label_id");
            entity.HasOne(e => e.Application).WithMany(e => e.ApplicationLabels)
                .HasForeignKey(e => new { e.OwnerUserId, e.ApplicationId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Label).WithMany(e => e.ApplicationLabels)
                .HasForeignKey(e => new { e.OwnerUserId, e.LabelId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.OwnerUserId, e.LabelId });
        });

        modelBuilder.Entity<PortMapping>(entity =>
        {
            entity.ToTable("port_mappings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.ServerId).HasColumnName("server_id").IsRequired();
            entity.Property(e => e.AppId).HasColumnName("app_id").IsRequired();
            entity.Property(e => e.PortNumber).HasColumnName("port_number").IsRequired();
            entity.Property(e => e.Protocol).HasColumnName("protocol").IsRequired();
            entity.HasAlternateKey(e => new { e.OwnerUserId, e.Id });
            entity.HasIndex(e => new { e.OwnerUserId, e.ServerId, e.PortNumber }).IsUnique();
            entity.HasOne(e => e.Server).WithMany(e => e.PortMappings)
                .HasForeignKey(e => new { e.OwnerUserId, e.ServerId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Application).WithMany(e => e.PortMappings)
                .HasForeignKey(e => new { e.OwnerUserId, e.AppId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppDependency>(entity =>
        {
            entity.ToTable("app_dependencies");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.SourceAppId).HasColumnName("source_app_id").IsRequired();
            entity.Property(e => e.DestAppId).HasColumnName("dest_app_id").IsRequired();
            entity.Property(e => e.DestPortId).HasColumnName("dest_port_id").IsRequired();
            entity.Property(e => e.ConnectionType).HasColumnName("connection_type").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.HasAlternateKey(e => new { e.OwnerUserId, e.Id });
            entity.HasIndex(e => new { e.OwnerUserId, e.SourceAppId, e.DestAppId, e.DestPortId }).IsUnique();
            entity.HasOne(e => e.SourceApplication).WithMany(e => e.SourceDependencies)
                .HasForeignKey(e => new { e.OwnerUserId, e.SourceAppId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.DestinationApplication).WithMany(e => e.DestinationDependencies)
                .HasForeignKey(e => new { e.OwnerUserId, e.DestAppId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.DestinationPort).WithMany(e => e.AppDependencies)
                .HasForeignKey(e => new { e.OwnerUserId, e.DestPortId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TopologyNode>(entity =>
        {
            entity.ToTable("topology_nodes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.NodeType).HasColumnName("node_type").IsRequired();
            entity.Property(e => e.Label).HasColumnName("label").IsRequired();
            entity.Property(e => e.X).HasColumnName("x"); entity.Property(e => e.Y).HasColumnName("y");
            entity.Property(e => e.Width).HasColumnName("width"); entity.Property(e => e.Height).HasColumnName("height");
            entity.Property(e => e.ParentNodeId).HasColumnName("parent_node_id"); entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
            entity.HasAlternateKey(e => new { e.OwnerUserId, e.Id });
            entity.HasOne(e => e.ParentNode).WithMany(e => e.ChildNodes)
                .HasForeignKey(e => new { e.OwnerUserId, e.ParentNodeId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.OwnerUserId);
        });

        modelBuilder.Entity<TopologyEdge>(entity =>
        {
            entity.ToTable("topology_edges");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.SourceNodeId).HasColumnName("source_node_id").IsRequired(); entity.Property(e => e.TargetNodeId).HasColumnName("target_node_id").IsRequired();
            entity.Property(e => e.SourceHandle).HasColumnName("source_handle").IsRequired(); entity.Property(e => e.TargetHandle).HasColumnName("target_handle").IsRequired();
            entity.Property(e => e.EdgeType).HasColumnName("edge_type").IsRequired(); entity.Property(e => e.Label).HasColumnName("label").IsRequired();
            entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
            entity.HasAlternateKey(e => new { e.OwnerUserId, e.Id });
            entity.HasOne(e => e.SourceNode).WithMany()
                .HasForeignKey(e => new { e.OwnerUserId, e.SourceNodeId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.TargetNode).WithMany()
                .HasForeignKey(e => new { e.OwnerUserId, e.TargetNodeId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.OwnerUserId, e.SourceNodeId, e.TargetNodeId, e.SourceHandle, e.TargetHandle }).IsUnique();
        });

        modelBuilder.Entity<LabelGrant>(entity =>
        {
            entity.ToTable("label_grants", table =>
            {
                table.HasCheckConstraint("ck_label_grants_subject", "(grantee_user_id IS NOT NULL AND token_hash IS NULL) OR (grantee_user_id IS NULL AND token_hash IS NOT NULL)");
                table.HasCheckConstraint("ck_label_grants_permission", "permission IN ('viewer', 'editor')");
                table.HasCheckConstraint("ck_label_grants_anonymous_viewer", "token_hash IS NULL OR permission = 'viewer'");
                table.HasCheckConstraint("ck_label_grants_token_expiry", "token_hash IS NULL OR expires_at IS NOT NULL");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id"); entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.LabelId).HasColumnName("label_id").IsRequired(); entity.Property(e => e.GranteeUserId).HasColumnName("grantee_user_id").HasMaxLength(100);
            entity.Property(e => e.Permission).HasColumnName("permission").HasMaxLength(20).IsRequired(); entity.Property(e => e.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at"); entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entity.Property(e => e.Version).HasColumnName("version").IsConcurrencyToken().IsRequired(); entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired(); entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasOne(e => e.Label).WithMany(e => e.Grants)
                .HasForeignKey(e => new { e.OwnerUserId, e.LabelId })
                .HasPrincipalKey(e => new { e.OwnerUserId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.LabelId, e.GranteeUserId }).IsUnique().HasFilter("revoked_at IS NULL AND grantee_user_id IS NOT NULL");
            entity.HasIndex(e => new { e.GranteeUserId, e.RevokedAt, e.ExpiresAt, e.LabelId }); entity.HasIndex(e => new { e.OwnerUserId, e.LabelId, e.RevokedAt });
        });

        modelBuilder.Entity<OwnerCatalogState>(entity =>
        {
            entity.ToTable("owner_catalog_states"); entity.HasKey(e => e.OwnerUserId);
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100);
            entity.Property(e => e.TopologyVersion).HasColumnName("topology_version").IsConcurrencyToken().IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });

        modelBuilder.Entity<TopologyView>(entity =>
        {
            entity.HasNoKey().ToView("v_topology_map");
            entity.Property(e => e.ServerId).HasColumnName("server_id"); entity.Property(e => e.ServerHostname).HasColumnName("server_hostname"); entity.Property(e => e.ServerIp).HasColumnName("server_ip");
            entity.Property(e => e.AppId).HasColumnName("app_id"); entity.Property(e => e.AppName).HasColumnName("app_name"); entity.Property(e => e.AppCode).HasColumnName("app_code");
            entity.Property(e => e.PortNumber).HasColumnName("port_number"); entity.Property(e => e.Protocol).HasColumnName("protocol"); entity.Property(e => e.Environment).HasColumnName("environment");
            entity.Property(e => e.DatacenterId).HasColumnName("datacenter_id"); entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100);
        });

        modelBuilder.Entity<DependencyView>(entity =>
        {
            entity.HasNoKey().ToView("v_dependency_graph");
            entity.Property(e => e.SourceAppId).HasColumnName("source_app_id"); entity.Property(e => e.SourceAppName).HasColumnName("source_app_name"); entity.Property(e => e.SourceAppCode).HasColumnName("source_app_code");
            entity.Property(e => e.DestAppId).HasColumnName("dest_app_id"); entity.Property(e => e.DestAppName).HasColumnName("dest_app_name"); entity.Property(e => e.DestAppCode).HasColumnName("dest_app_code");
            entity.Property(e => e.DestPortNumber).HasColumnName("dest_port_number"); entity.Property(e => e.ConnectionType).HasColumnName("connection_type"); entity.Property(e => e.Environment).HasColumnName("environment");
            entity.Property(e => e.DatacenterId).HasColumnName("datacenter_id"); entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100);
        });
    }
}
