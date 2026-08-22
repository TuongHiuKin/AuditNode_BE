using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace AuditNode.API.Routing;

public sealed class LowercaseRouteTokenTransformer : IOutboundParameterTransformer
{
    public string? TransformOutbound(object? value) => value?.ToString()?.ToLowerInvariant();
}
