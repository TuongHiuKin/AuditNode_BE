namespace AuditNode.Application.Interfaces;

public interface ICatalogCursorProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedPayload);
}
