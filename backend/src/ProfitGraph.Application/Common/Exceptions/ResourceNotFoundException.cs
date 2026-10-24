namespace ProfitGraph.Application.Common.Exceptions;

public sealed class ResourceNotFoundException(string message, string code = "resource_not_found") : ApplicationExceptionBase(code, message);