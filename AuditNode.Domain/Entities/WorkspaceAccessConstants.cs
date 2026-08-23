namespace AuditNode.Domain.Entities;

public static class WorkspaceRoles
{
    public const string Owner = "owner";
    public const string Admin = "workspace_admin";
    public const string Auditor = "auditor";
    public const string Viewer = "viewer";
}

public static class WorkspaceScopeModes
{
    public const string All = "all";
    public const string Labels = "labels";
    public const string Frames = "frames";
}

public static class WorkspaceScopeTypes
{
    public const string Label = "label";
    public const string Frame = "frame";
}
