using Microsoft.AspNetCore.Identity;

namespace AuditNode.Infrastructure.Identity;

/// <summary>
/// Application identity, kept in Infrastructure because it inherits the ASP.NET
/// Identity framework type. Domain stays persistence- and framework-agnostic;
/// catalog tables reference a user only through the string <c>OwnerUserId</c>.
/// </summary>
public class AppUser : IdentityUser
{
    public AppUser()
    {
        // Default IdentityUser leaves Id null until the store assigns it. AuditNode
        // needs a stable owner key from the moment the object exists.
        Id = Guid.NewGuid().ToString();
    }
}
