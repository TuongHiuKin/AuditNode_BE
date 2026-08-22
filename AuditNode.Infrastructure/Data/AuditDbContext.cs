using Microsoft.EntityFrameworkCore;
using System.Linq;
using AuditNode.Domain.Entities;
using AppEntity = AuditNode.Domain.Entities.Application;
using AuditNode.Application.Interfaces;

namespace AuditNode.Infrastructure.Data;

public class AuditDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    public AuditDbContext(DbContextOptions<AuditDbContext> options, ITenantProvider tenantProvider) : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    public DbSet<Datacenter> Datacenters { get; set; }
    public DbSet<Server> Servers { get; set; }
    public DbSet<AppEntity> Applications { get; set; }
    public DbSet<PortMapping> PortMappings { get; set; }
    public DbSet<AppDependency> AppDependencies { get; set; }
    public DbSet<TopologyView> TopologyViews { get; set; }
    public DbSet<DependencyView> DependencyViews { get; set; }
    public DbSet<Workspace> Workspaces { get; set; }
    public DbSet<WorkspaceMember> WorkspaceMembers { get; set; }
    public DbSet<TopologyNode> TopologyNodes { get; set; }
    public DbSet<TopologyEdge> TopologyEdges { get; set; }
    public DbSet<Label> Labels { get; set; }
    public DbSet<ServerLabel> ServerLabels { get; set; }
    public DbSet<ApplicationLabel> ApplicationLabels { get; set; }
    public Guid? CurrentWorkspaceId => _tenantProvider.WorkspaceId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // TopologyNode
        modelBuilder.Entity<TopologyNode>(entity =>
        {
            entity.ToTable("topology_nodes");
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.WorkspaceId, e.Id });
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired().ValueGeneratedNever();
            entity.Property(e => e.NodeType).HasColumnName("node_type").IsRequired();
            entity.Property(e => e.Label).HasColumnName("label").IsRequired();
            entity.Property(e => e.X).HasColumnName("x");
            entity.Property(e => e.Y).HasColumnName("y");
            entity.Property(e => e.Width).HasColumnName("width");
            entity.Property(e => e.Height).HasColumnName("height");
            entity.Property(e => e.ParentNodeId).HasColumnName("parent_node_id");
            entity.Property(e => e.ReferenceId).HasColumnName("reference_id");

            entity.HasOne(e => e.ParentNode)
                .WithMany(e => e.ChildNodes)
                .HasForeignKey(e => new { e.WorkspaceId, e.ParentNodeId })
                .HasPrincipalKey(e => new { e.WorkspaceId, e.Id })
                .OnDelete(DeleteBehavior.Cascade); // If a group is deleted, delete child node visual states

            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.WorkspaceId);

            entity.HasQueryFilter(e => e.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        // Workspace
        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.IsPersonal).HasColumnName("is_personal").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasIndex(e => e.OwnerUserId)
                .IsUnique()
                .HasFilter("is_personal = true");
        });

        modelBuilder.Entity<WorkspaceMember>(entity =>
        {
            entity.ToTable("workspace_members", table => table.HasCheckConstraint(
                "ck_workspace_members_role",
                "role IN ('workspace_admin', 'editor', 'auditor', 'viewer')"));
            entity.HasKey(e => new { e.WorkspaceId, e.UserId });
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Role).HasColumnName("role").HasMaxLength(40).IsRequired();
            entity.Property(e => e.InvitedByUserId).HasColumnName("invited_by_user_id").HasMaxLength(100).IsRequired();
            entity.Property(e => e.JoinedAt).HasColumnName("joined_at").IsRequired();
            entity.HasIndex(e => e.UserId);
            entity.HasOne(e => e.Workspace)
                .WithMany(e => e.Members)
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Datacenter
        modelBuilder.Entity<Datacenter>(entity =>
        {
            entity.ToTable("datacenters");
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.WorkspaceId, e.Id });
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired().ValueGeneratedNever();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Location).HasColumnName("location").IsRequired();
            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.WorkspaceId);
            entity.HasQueryFilter(e => e.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        modelBuilder.Entity<TopologyEdge>(entity =>
        {
            entity.ToTable("topology_edges");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired().ValueGeneratedNever();
            entity.Property(e => e.SourceNodeId).HasColumnName("source_node_id").IsRequired();
            entity.Property(e => e.TargetNodeId).HasColumnName("target_node_id").IsRequired();
            entity.Property(e => e.SourceHandle).HasColumnName("source_handle").IsRequired();
            entity.Property(e => e.TargetHandle).HasColumnName("target_handle").IsRequired();
            entity.Property(e => e.EdgeType).HasColumnName("edge_type").IsRequired();
            entity.Property(e => e.Label).HasColumnName("label").IsRequired();
            entity.Property(e => e.ReferenceId).HasColumnName("reference_id");

            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SourceNode)
                .WithMany()
                .HasForeignKey(e => new { e.WorkspaceId, e.SourceNodeId })
                .HasPrincipalKey(e => new { e.WorkspaceId, e.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.TargetNode)
                .WithMany()
                .HasForeignKey(e => new { e.WorkspaceId, e.TargetNodeId })
                .HasPrincipalKey(e => new { e.WorkspaceId, e.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new
            {
                e.WorkspaceId,
                e.SourceNodeId,
                e.TargetNodeId,
                e.SourceHandle,
                e.TargetHandle
            }).IsUnique();
            entity.HasQueryFilter(e => e.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        // Server
        modelBuilder.Entity<Server>(entity =>
        {
            entity.ToTable("servers");
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.WorkspaceId, e.Id });
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired().ValueGeneratedNever();
            entity.Property(e => e.DatacenterId).HasColumnName("datacenter_id").IsRequired();
            entity.Property(e => e.IpAddress).HasColumnName("ip_address").IsRequired();
            entity.Property(e => e.Hostname).HasColumnName("hostname").IsRequired();
            entity.Property(e => e.OsType).HasColumnName("os_type").IsRequired();
            entity.Property(e => e.Environment).HasColumnName("environment").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();

            entity.HasIndex(e => new { e.WorkspaceId, e.IpAddress }).IsUnique();

            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(s => s.Datacenter)
                .WithMany(d => d.Servers)
                .HasForeignKey(s => new { s.WorkspaceId, s.DatacenterId })
                .HasPrincipalKey(d => new { d.WorkspaceId, d.Id })
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(s => s.Labels)
                .WithMany(l => l.Servers)
                .UsingEntity<ServerLabel>(
                    right => right.HasOne(link => link.Label)
                        .WithMany(label => label.ServerLabels)
                        .HasForeignKey(link => new { link.WorkspaceId, link.LabelId })
                        .HasPrincipalKey(label => new { label.WorkspaceId, label.Id })
                        .OnDelete(DeleteBehavior.Cascade),
                    left => left.HasOne(link => link.Server)
                        .WithMany(server => server.ServerLabels)
                        .HasForeignKey(link => new { link.WorkspaceId, link.ServerId })
                        .HasPrincipalKey(server => new { server.WorkspaceId, server.Id })
                        .OnDelete(DeleteBehavior.Cascade),
                    join =>
                    {
                        join.ToTable("server_labels");
                        join.HasKey(link => new { link.WorkspaceId, link.ServerId, link.LabelId });
                        join.Property(link => link.WorkspaceId).HasColumnName("workspace_id").IsRequired().ValueGeneratedNever();
                        join.Property(link => link.ServerId).HasColumnName("server_id").IsRequired();
                        join.Property(link => link.LabelId).HasColumnName("label_id").IsRequired();
                        join.HasOne(link => link.Workspace)
                            .WithMany()
                            .HasForeignKey(link => link.WorkspaceId)
                            .OnDelete(DeleteBehavior.Restrict);
                        join.HasIndex(link => new { link.WorkspaceId, link.LabelId });
                        join.HasQueryFilter(link => link.WorkspaceId == _tenantProvider.WorkspaceId);
                    });

            entity.HasQueryFilter(s => s.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        // Label
        modelBuilder.Entity<Label>(entity =>
        {
            entity.ToTable("labels");
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.WorkspaceId, e.Id });
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired().ValueGeneratedNever();
            entity.Property(e => e.Key).HasColumnName("key").IsRequired();
            entity.Property(e => e.Value).HasColumnName("value").IsRequired();

            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.WorkspaceId, e.Key, e.Value });

            entity.HasQueryFilter(e => e.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        modelBuilder.Entity<ApplicationLabel>(entity =>
        {
            entity.ToTable("application_labels");
            entity.HasKey(e => new { e.WorkspaceId, e.ApplicationId, e.LabelId });
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired().ValueGeneratedNever();
            entity.Property(e => e.ApplicationId).HasColumnName("application_id").IsRequired();
            entity.Property(e => e.LabelId).HasColumnName("label_id").IsRequired();

            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Application)
                .WithMany(e => e.ApplicationLabels)
                .HasForeignKey(e => new { e.WorkspaceId, e.ApplicationId })
                .HasPrincipalKey(e => new { e.WorkspaceId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Label)
                .WithMany(e => e.ApplicationLabels)
                .HasForeignKey(e => new { e.WorkspaceId, e.LabelId })
                .HasPrincipalKey(e => new { e.WorkspaceId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.WorkspaceId, e.LabelId });
            entity.HasQueryFilter(e => e.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        // Application
        modelBuilder.Entity<AppEntity>(entity =>
        {
            entity.ToTable("applications");
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.WorkspaceId, e.Id });
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired().ValueGeneratedNever();
            entity.Property(e => e.AppCode).HasColumnName("app_code").IsRequired();
            entity.Property(e => e.AppName).HasColumnName("app_name").IsRequired();
            entity.Property(e => e.OwnerTeam)
                .HasColumnName("owner_team")
                .HasMaxLength(255)
                .IsRequired();
            entity.Property(e => e.Risk).HasColumnName("risk").IsRequired();
            entity.Property(e => e.Icon).HasColumnName("icon");
            entity.Property(e => e.TechStack).HasColumnName("tech_stack");

            entity.HasIndex(e => new { e.WorkspaceId, e.AppCode }).IsUnique();

            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(a => a.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        // PortMapping
        modelBuilder.Entity<PortMapping>(entity =>
        {
            entity.ToTable("port_mappings");
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.WorkspaceId, e.Id });
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired().ValueGeneratedNever();
            entity.Property(e => e.ServerId).HasColumnName("server_id").IsRequired();
            entity.Property(e => e.AppId).HasColumnName("app_id").IsRequired();
            entity.Property(e => e.PortNumber).HasColumnName("port_number").IsRequired();
            entity.Property(e => e.Protocol).HasColumnName("protocol").IsRequired();

            entity.HasIndex(e => new { e.WorkspaceId, e.ServerId, e.PortNumber }).IsUnique();
            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(pm => pm.Server)
                .WithMany(s => s.PortMappings)
                .HasForeignKey(pm => new { pm.WorkspaceId, pm.ServerId })
                .HasPrincipalKey(s => new { s.WorkspaceId, s.Id })
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(pm => pm.Application)
                .WithMany(a => a.PortMappings)
                .HasForeignKey(pm => new { pm.WorkspaceId, pm.AppId })
                .HasPrincipalKey(a => new { a.WorkspaceId, a.Id })
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(pm => pm.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        // AppDependency
        modelBuilder.Entity<AppDependency>(entity =>
        {
            entity.ToTable("app_dependencies");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired().ValueGeneratedNever();
            entity.Property(e => e.SourceAppId).HasColumnName("source_app_id").IsRequired();
            entity.Property(e => e.DestAppId).HasColumnName("dest_app_id").IsRequired();
            entity.Property(e => e.DestPortId).HasColumnName("dest_port_id").IsRequired();
            entity.Property(e => e.ConnectionType).HasColumnName("connection_type").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(e => e.WorkspaceId);
            entity.HasIndex(e => new { e.WorkspaceId, e.SourceAppId, e.DestAppId, e.DestPortId })
                .IsUnique();
            entity.HasOne(e => e.Workspace)
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(ad => ad.SourceApplication)
                .WithMany(a => a.SourceDependencies)
                .HasForeignKey(ad => new { ad.WorkspaceId, ad.SourceAppId })
                .HasPrincipalKey(a => new { a.WorkspaceId, a.Id })
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ad => ad.DestinationApplication)
                .WithMany(a => a.DestinationDependencies)
                .HasForeignKey(ad => new { ad.WorkspaceId, ad.DestAppId })
                .HasPrincipalKey(a => new { a.WorkspaceId, a.Id })
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ad => ad.DestinationPort)
                .WithMany(pm => pm.AppDependencies)
                .HasForeignKey(ad => new { ad.WorkspaceId, ad.DestPortId })
                .HasPrincipalKey(pm => new { pm.WorkspaceId, pm.Id })
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(ad => ad.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        // Configure read-only views
        modelBuilder.Entity<TopologyView>(entity =>
        {
            entity.HasNoKey().ToView("v_topology_map");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
            entity.Property(e => e.ServerId).HasColumnName("server_id");
            entity.Property(e => e.ServerHostname).HasColumnName("server_hostname");
            entity.Property(e => e.ServerIp).HasColumnName("server_ip");
            entity.Property(e => e.AppId).HasColumnName("app_id");
            entity.Property(e => e.AppName).HasColumnName("app_name");
            entity.Property(e => e.AppCode).HasColumnName("app_code");
            entity.Property(e => e.PortNumber).HasColumnName("port_number");
            entity.Property(e => e.Protocol).HasColumnName("protocol");
            entity.Property(e => e.Environment).HasColumnName("environment");
            entity.Property(e => e.DatacenterId).HasColumnName("datacenter_id");
            entity.HasQueryFilter(e => e.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        modelBuilder.Entity<DependencyView>(entity =>
        {
            entity.HasNoKey().ToView("v_dependency_graph");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
            entity.Property(e => e.SourceAppId).HasColumnName("source_app_id");
            entity.Property(e => e.SourceAppName).HasColumnName("source_app_name");
            entity.Property(e => e.SourceAppCode).HasColumnName("source_app_code");
            entity.Property(e => e.DestAppId).HasColumnName("dest_app_id");
            entity.Property(e => e.DestAppName).HasColumnName("dest_app_name");
            entity.Property(e => e.DestAppCode).HasColumnName("dest_app_code");
            entity.Property(e => e.DestPortNumber).HasColumnName("dest_port_number");
            entity.Property(e => e.ConnectionType).HasColumnName("connection_type");
            entity.Property(e => e.Environment).HasColumnName("environment");
            entity.Property(e => e.DatacenterId).HasColumnName("datacenter_id");
            entity.HasQueryFilter(e => e.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var workspaceIdProp = entityType.FindProperty("WorkspaceId");
            if (workspaceIdProp != null && workspaceIdProp.ClrType == typeof(Guid))
            {
                workspaceIdProp.Sentinel = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyWorkspaceId();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyWorkspaceId();
        return base.SaveChanges();
    }

    private void ApplyWorkspaceId()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => (e.State == EntityState.Added || e.State == EntityState.Modified));

        foreach (var entry in entries)
        {
            if (entry.Entity is Server server)
            {
                if (_tenantProvider.WorkspaceId.HasValue )
                {
                    server.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is Datacenter datacenter)
            {
                if (_tenantProvider.WorkspaceId.HasValue )
                {
                    datacenter.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is AppEntity app)
            {
                if (_tenantProvider.WorkspaceId.HasValue )
                {
                    app.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is TopologyNode node)
            {
                if (_tenantProvider.WorkspaceId.HasValue )
                {
                    node.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is TopologyEdge edge)
            {
                if (_tenantProvider.WorkspaceId.HasValue )
                {
                    edge.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is PortMapping pm)
            {
                if (_tenantProvider.WorkspaceId.HasValue )
                {
                    pm.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is AppDependency ad)
            {
                if (_tenantProvider.WorkspaceId.HasValue )
                {
                    ad.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is Label label)
            {
                if (_tenantProvider.WorkspaceId.HasValue )
                {
                    label.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is ApplicationLabel applicationLabel)
            {
                if (_tenantProvider.WorkspaceId.HasValue )
                {
                    applicationLabel.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is ServerLabel serverLabel)
            {
                if (_tenantProvider.WorkspaceId.HasValue )
                {
                    serverLabel.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
        }
    }
}
