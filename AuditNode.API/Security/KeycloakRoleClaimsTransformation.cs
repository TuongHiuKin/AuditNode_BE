using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Text.Json;

namespace AuditNode.API.Security;

public class KeycloakRoleClaimsTransformation : IClaimsTransformation
{
    private const string RealmAccessClaimType = "realm_access";
    private const string RolesJsonPropertyName = "roles";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal == null || !principal.Identity?.IsAuthenticated == true)
        {
            return Task.FromResult(principal ?? new ClaimsPrincipal());
        }

        var identity = principal.Identity as ClaimsIdentity;
        if (identity == null)
        {
            return Task.FromResult(principal);
        }

        var realmAccessClaims = principal.FindAll(RealmAccessClaimType).ToList();
        if (!realmAccessClaims.Any())
        {
            return Task.FromResult(principal);
        }

        foreach (var claim in realmAccessClaims)
        {
            if (string.IsNullOrWhiteSpace(claim.Value))
            {
                continue;
            }

            try
            {
                using var jsonDocument = JsonDocument.Parse(claim.Value);
                var root = jsonDocument.RootElement;

                JsonElement rolesArray = default;
                bool isArrayFound = false;

                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty(RolesJsonPropertyName, out var rolesElement) &&
                    rolesElement.ValueKind == JsonValueKind.Array)
                {
                    rolesArray = rolesElement;
                    isArrayFound = true;
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    rolesArray = root;
                    isArrayFound = true;
                }

                if (isArrayFound)
                {
                    foreach (var roleElement in rolesArray.EnumerateArray())
                    {
                        if (roleElement.ValueKind == JsonValueKind.String)
                        {
                            var roleValue = roleElement.GetString();
                            if (!string.IsNullOrWhiteSpace(roleValue) &&
                                !identity.HasClaim(ClaimTypes.Role, roleValue))
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Role, roleValue));
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Safely ignore malformed JSON in realm_access claim
            }
        }

        return Task.FromResult(principal);
    }
}
