namespace AuditNode.Domain.Entities;

/// <summary>
/// A third-party service the catalog depends on but does not own — a payment
/// provider, a mail relay, a partner API. It lives outside the owner's
/// infrastructure, so it has no server, datacenter or deployment row.
///
/// Kept per-owner like every catalog aggregate: two teams that both call Stripe
/// record it separately, and neither can see the other's entry.
/// </summary>
public class ExternalService
{
    public Guid Id { get; set; }
    public string? OwnerUserId { get; set; }

    /// <summary>Display name, e.g. "Stripe payments".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// What kind of dependency this is. Free text rather than an enum so a team is
    /// never blocked by a missing category; the API validates it as a short token.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Base endpoint, e.g. "https://api.stripe.com". Optional.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Vendor or provider name, e.g. "Stripe, Inc.".</summary>
    public string? Provider { get; set; }

    public string? Protocol { get; set; }
    public int? Port { get; set; }

    /// <summary>Environment this dependency is used from.</summary>
    public string Environment { get; set; } = string.Empty;

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AppDependency> IncomingDependencies { get; set; } = new List<AppDependency>();
}
