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
    public DbSet<TopologyNode> TopologyNodes { get; set; }
    public DbSet<Label> Labels { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // TopologyNode
        modelBuilder.Entity<TopologyNode>(entity =>
        {
            entity.ToTable("topology_nodes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired();
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
                .HasForeignKey(e => e.ParentNodeId)
                .OnDelete(DeleteBehavior.Cascade); // If a group is deleted, delete child node visual states

            entity.HasQueryFilter(e => e.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        // Workspace
        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Description).HasColumnName("description");
        });

        // Datacenter
        modelBuilder.Entity<Datacenter>(entity =>
        {
            entity.ToTable("datacenters");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Location).HasColumnName("location").IsRequired();
        });

        // Server
        modelBuilder.Entity<Server>(entity =>
        {
            entity.ToTable("servers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired();
            entity.Property(e => e.DatacenterId).HasColumnName("datacenter_id").IsRequired();
            entity.Property(e => e.IpAddress).HasColumnName("ip_address").IsRequired();
            entity.Property(e => e.Hostname).HasColumnName("hostname").IsRequired();
            entity.Property(e => e.OsType).HasColumnName("os_type").IsRequired();
            entity.Property(e => e.Environment).HasColumnName("environment").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();

            entity.HasIndex(e => e.IpAddress).IsUnique();

            entity.HasOne(s => s.Datacenter)
                .WithMany(d => d.Servers)
                .HasForeignKey(s => s.DatacenterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(s => s.Labels)
                .WithMany(l => l.Servers)
                .UsingEntity("server_labels");

            entity.HasQueryFilter(s => s.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        // Label
        modelBuilder.Entity<Label>(entity =>
        {
            entity.ToTable("labels");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired();
            entity.Property(e => e.Key).HasColumnName("key").IsRequired();
            entity.Property(e => e.Value).HasColumnName("value").IsRequired();

            entity.HasQueryFilter(e => e.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        // Application
        modelBuilder.Entity<AppEntity>(entity =>
        {
            entity.ToTable("applications");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired();
            entity.Property(e => e.AppCode).HasColumnName("app_code").IsRequired();
            entity.Property(e => e.AppName).HasColumnName("app_name").IsRequired();
            entity.Property(e => e.OwnerTeam)
                .HasColumnName("owner_team")
                .HasMaxLength(255)
                .IsRequired();
            entity.Property(e => e.Risk).HasColumnName("risk").IsRequired();
            entity.Property(e => e.Icon).HasColumnName("icon");
            entity.Property(e => e.TechStack).HasColumnName("tech_stack");

            entity.HasIndex(e => e.AppCode).IsUnique();

            entity.HasQueryFilter(a => a.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        // PortMapping
        modelBuilder.Entity<PortMapping>(entity =>
        {
            entity.ToTable("port_mappings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired();
            entity.Property(e => e.ServerId).HasColumnName("server_id").IsRequired();
            entity.Property(e => e.AppId).HasColumnName("app_id").IsRequired();
            entity.Property(e => e.PortNumber).HasColumnName("port_number").IsRequired();
            entity.Property(e => e.Protocol).HasColumnName("protocol").IsRequired();

            entity.HasOne(pm => pm.Server)
                .WithMany(s => s.PortMappings)
                .HasForeignKey(pm => pm.ServerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(pm => pm.Application)
                .WithMany(a => a.PortMappings)
                .HasForeignKey(pm => pm.AppId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(pm => pm.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        // AppDependency
        modelBuilder.Entity<AppDependency>(entity =>
        {
            entity.ToTable("app_dependencies");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id").IsRequired();
            entity.Property(e => e.SourceAppId).HasColumnName("source_app_id").IsRequired();
            entity.Property(e => e.DestAppId).HasColumnName("dest_app_id").IsRequired();
            entity.Property(e => e.DestPortId).HasColumnName("dest_port_id").IsRequired();
            entity.Property(e => e.ConnectionType).HasColumnName("connection_type").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasOne(ad => ad.SourceApplication)
                .WithMany(a => a.SourceDependencies)
                .HasForeignKey(ad => ad.SourceAppId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ad => ad.DestinationApplication)
                .WithMany(a => a.DestinationDependencies)
                .HasForeignKey(ad => ad.DestAppId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ad => ad.DestinationPort)
                .WithMany(pm => pm.AppDependencies)
                .HasForeignKey(ad => ad.DestPortId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(ad => ad.WorkspaceId == _tenantProvider.WorkspaceId);
        });

        // Configure read-only views
        modelBuilder.Entity<TopologyView>(entity =>
        {
            entity.HasNoKey().ToView("v_topology_map");
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
        });

        modelBuilder.Entity<DependencyView>(entity =>
        {
            entity.HasNoKey().ToView("v_dependency_graph");
            entity.Property(e => e.SourceAppId).HasColumnName("source_app_id");
            entity.Property(e => e.SourceAppName).HasColumnName("source_app_name");
            entity.Property(e => e.SourceAppCode).HasColumnName("source_app_code");
            entity.Property(e => e.DestAppId).HasColumnName("dest_app_id");
            entity.Property(e => e.DestAppName).HasColumnName("dest_app_name");
            entity.Property(e => e.DestAppCode).HasColumnName("dest_app_code");
            entity.Property(e => e.DestPortNumber).HasColumnName("dest_port_number");
            entity.Property(e => e.ConnectionType).HasColumnName("connection_type");
            entity.Property(e => e.DestServerHostname).HasColumnName("dest_server_hostname");
            entity.Property(e => e.Environment).HasColumnName("environment");
            entity.Property(e => e.DatacenterId).HasColumnName("datacenter_id");
        });
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
                if (_tenantProvider.WorkspaceId.HasValue)
                {
                    server.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is AppEntity app)
            {
                if (_tenantProvider.WorkspaceId.HasValue)
                {
                    app.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is TopologyNode node)
            {
                if (_tenantProvider.WorkspaceId.HasValue)
                {
                    node.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is PortMapping pm)
            {
                if (_tenantProvider.WorkspaceId.HasValue)
                {
                    pm.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is AppDependency ad)
            {
                if (_tenantProvider.WorkspaceId.HasValue)
                {
                    ad.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
            else if (entry.Entity is Label label)
            {
                if (_tenantProvider.WorkspaceId.HasValue)
                {
                    label.WorkspaceId = _tenantProvider.WorkspaceId.Value;
                }
            }
        }
    }
}
