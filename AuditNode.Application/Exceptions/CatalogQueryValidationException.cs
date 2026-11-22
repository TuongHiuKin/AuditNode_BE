namespace AuditNode.Application.Exceptions;

public sealed class CatalogQueryValidationException(string message) : Exception(message);
