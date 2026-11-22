using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public sealed class OwnerLabelService : IOwnerLabelService
{
    private readonly AuditDbContext _context;
    private readonly TimeProvider _timeProvider;

    public OwnerLabelService(AuditDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task EnsureAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);

        if (await _context.Labels.AnyAsync(
                label => label.OwnerUserId == ownerUserId && label.Kind == LabelKinds.Owner,
                cancellationToken))
            return;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var ownerLabel = new Label
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Key = "Owner",
            Value = ownerUserId,
            Kind = LabelKinds.Owner,
            IsProtected = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.Labels.Add(ownerLabel);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.Entry(ownerLabel).State = EntityState.Detached;
            if (await _context.Labels.AnyAsync(
                    label => label.OwnerUserId == ownerUserId && label.Kind == LabelKinds.Owner,
                    cancellationToken))
                return;
            throw;
        }
    }
}
