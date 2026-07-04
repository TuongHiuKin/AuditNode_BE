using System.ComponentModel.DataAnnotations.Schema;

namespace AuditNode.Domain.Entities;

public class BoundaryFrame
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("x_position")]
    public double XPosition { get; set; }

    [Column("y_position")]
    public double YPosition { get; set; }

    [Column("width")]
    public double Width { get; set; }

    [Column("height")]
    public double Height { get; set; }

    [Column("owner_id")]
    public Guid OwnerId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public virtual ICollection<Server> Servers { get; set; } = new List<Server>();
    public virtual ICollection<Application> Applications { get; set; } = new List<Application>();
}
