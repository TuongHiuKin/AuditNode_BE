using AuditNode.API.Security;
using AuditNode.Application.DTOs;
using AuditNode.Application.Exceptions;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace AuditNode.Tests.Services;

public sealed class CatalogCursorSecurityTests
{
    private readonly CatalogCursorCodec _codec = new(
        new DataProtectionCatalogCursorProtector(new EphemeralDataProtectionProvider()));

    [Fact]
    public void Protected_cursor_round_trips_with_stable_binding_schema()
    {
        var id = Guid.NewGuid();
        var cursor = _codec.Encode("applications", CatalogView.Shared, "user-a", "labels:tier=critical", ["APP"], id);

        var decoded = _codec.Decode("applications", CatalogView.Shared, "user-a", "labels:tier=critical", cursor, 1);

        decoded.Should().BeEquivalentTo(new CatalogCursorPosition(["APP"], id));
        cursor.Should().NotContain("applications").And.NotContain("user-a").And.NotContain("critical");
    }

    [Fact]
    public void Protected_cursor_rejects_tamper_cross_user_cross_view_and_cross_filter()
    {
        var cursor = _codec.Encode("search", CatalogView.Shared, "user-a", "q=alpha", ["APP", "Alpha"], Guid.NewGuid());
        var tampered = cursor[..^1] + (cursor[^1] == 'A' ? 'B' : 'A');

        Action tamper = () => _codec.Decode("search", CatalogView.Shared, "user-a", "q=alpha", tampered, 2);
        Action crossUser = () => _codec.Decode("search", CatalogView.Shared, "user-b", "q=alpha", cursor, 2);
        Action crossView = () => _codec.Decode("search", CatalogView.Mine, "user-a", "q=alpha", cursor, 2);
        Action crossFilter = () => _codec.Decode("search", CatalogView.Shared, "user-a", "q=beta", cursor, 2);

        tamper.Should().Throw<CatalogQueryValidationException>();
        crossUser.Should().Throw<CatalogQueryValidationException>();
        crossView.Should().Throw<CatalogQueryValidationException>();
        crossFilter.Should().Throw<CatalogQueryValidationException>();
    }

    [Fact]
    public void Application_filter_fingerprint_matches_case_sensitive_database_semantics()
    {
        CatalogFilterFingerprint.Applications(" Scope ", "Production")
            .Should().Be(CatalogFilterFingerprint.Applications("Scope", "Production"));
        CatalogFilterFingerprint.Applications("Scope", "Production")
            .Should().NotBe(CatalogFilterFingerprint.Applications("scope", "production"));
    }
}
