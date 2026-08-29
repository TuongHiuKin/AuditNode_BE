using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public sealed class LabelShareOptionsService(
    AuditDbContext context,
    IIdentityAdminService identities,
    ICurrentUserService currentUser) : ILabelShareOptionsService
{
    private const int DirectoryCandidateLimit = 100;

    public async Task<LabelShareOptionsDto?> GetAsync(
        Guid labelId,
        string search,
        int first,
        int max,
        CancellationToken cancellationToken = default)
    {
        var actorUserId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(actorUserId)) return null;

        var labelKind = await context.Labels.IgnoreQueryFilters().AsNoTracking()
            .Where(label => label.Id == labelId &&
                            label.OwnerUserId != null &&
                            label.OwnerUserId == actorUserId)
            .Select(label => label.Kind)
            .SingleOrDefaultAsync(cancellationToken);
        if (labelKind is null) return null;

        var sharesAllOwnerResources = labelKind == LabelKinds.Owner;
        var warningCode = sharesAllOwnerResources
            ? LabelShareWarningCodes.OwnerLabelSharesAllOwnerResources
            : null;
        var normalizedSearch = search.Trim();
        if (normalizedSearch.Length is < 3 or > 100)
            return new LabelShareOptionsDto([], sharesAllOwnerResources, warningCode);

        var users = (await identities.ListUsersAsync(
                normalizedSearch,
                0,
                DirectoryCandidateLimit,
                cancellationToken))
            .Take(DirectoryCandidateLimit)
            .Where(user => user.Enabled &&
                           !string.Equals(user.Id, actorUserId, StringComparison.Ordinal))
            .OrderBy(user => MatchRank(user, normalizedSearch))
            .ThenBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
            .Skip(Math.Clamp(first, 0, DirectoryCandidateLimit))
            .Take(Math.Clamp(max, 1, 20))
            .Select(user => new LabelShareOptionUserDto(user.Id, user.Username, user.Email))
            .ToList();

        return new LabelShareOptionsDto(users, sharesAllOwnerResources, warningCode);
    }

    private static int MatchRank(IdentityAdminUserDto user, string search)
    {
        if (string.Equals(user.Username, search, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(user.Email, search, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (user.Username.StartsWith(search, StringComparison.OrdinalIgnoreCase) ||
            (user.Email?.StartsWith(search, StringComparison.OrdinalIgnoreCase) ?? false))
            return 1;
        return 2;
    }
}
