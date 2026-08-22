using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Errors;

public static class ApiProblem
{
    public static ProblemDetails Create(HttpContext? context, int status, string title)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        if (!string.IsNullOrWhiteSpace(context?.TraceIdentifier))
            problem.Extensions["correlationId"] = context.TraceIdentifier;
        return problem;
    }
}
