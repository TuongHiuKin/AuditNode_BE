using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace AuditNode.API.Security;

public sealed class DataProtectionCatalogCursorProtector(IDataProtectionProvider provider) : ICatalogCursorProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("AuditNode.GlobalCatalog.Cursor.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string protectedPayload) => _protector.Unprotect(protectedPayload);
}
