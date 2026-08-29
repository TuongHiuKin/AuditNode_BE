namespace AuditNode.API.Middleware;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class SkipWorkspaceValidationAttribute : Attribute;
