using System.Text.Json;
using System.Security.Cryptography;
using AuditNode.Application.DTOs;
using AuditNode.Application.Exceptions;
using AuditNode.Application.Interfaces;

namespace AuditNode.Infrastructure.Services;

public sealed class CatalogCursorCodec(ICatalogCursorProtector protector) : ICatalogCursorCodec
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Encode(string endpoint, CatalogView view, string principalBinding, string filterFingerprint, IReadOnlyList<string> sortValues, Guid id)
    {
        var payload = JsonSerializer.Serialize(
            new CursorPayload(CurrentVersion, endpoint, ViewName(view), principalBinding, filterFingerprint, sortValues, id),
            JsonOptions);
        return protector.Protect(payload);
    }

    public CatalogCursorPosition Decode(string endpoint, CatalogView view, string principalBinding, string filterFingerprint, string cursor, int expectedSortValues)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 4096)
                throw Invalid();

            var payload = JsonSerializer.Deserialize<CursorPayload>(protector.Unprotect(cursor), JsonOptions);
            if (payload is null || payload.Version != CurrentVersion ||
                !string.Equals(payload.Endpoint, endpoint, StringComparison.Ordinal) ||
                !string.Equals(payload.View, ViewName(view), StringComparison.Ordinal) ||
                !string.Equals(payload.PrincipalBinding, principalBinding, StringComparison.Ordinal) ||
                !string.Equals(payload.FilterFingerprint, filterFingerprint, StringComparison.Ordinal) ||
                payload.Id == Guid.Empty || payload.SortValues is null ||
                payload.SortValues.Count != expectedSortValues || payload.SortValues.Any(value => value is null))
                throw Invalid();

            return new CatalogCursorPosition(payload.SortValues, payload.Id);
        }
        catch (CatalogQueryValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or CryptographicException)
        {
            throw Invalid();
        }
    }

    private static string ViewName(CatalogView view) => view switch
    {
        CatalogView.Mine => "mine",
        CatalogView.Shared => "shared",
        _ => throw new CatalogQueryValidationException("Catalog view must be 'mine' or 'shared'.")
    };

    private static CatalogQueryValidationException Invalid() =>
        new("The catalog cursor is malformed or does not belong to this endpoint and view.");

    private sealed record CursorPayload(
        int Version,
        string Endpoint,
        string View,
        string PrincipalBinding,
        string FilterFingerprint,
        IReadOnlyList<string> SortValues,
        Guid Id);
}
