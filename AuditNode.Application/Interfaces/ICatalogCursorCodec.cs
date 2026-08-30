using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface ICatalogCursorCodec
{
    string Encode(string endpoint, CatalogView view, string principalBinding, string filterFingerprint, IReadOnlyList<string> sortValues, Guid id);
    CatalogCursorPosition Decode(string endpoint, CatalogView view, string principalBinding, string filterFingerprint, string cursor, int expectedSortValues);
}
