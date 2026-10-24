namespace ProfitGraph.Application.Common.Exceptions;

public sealed class ResourceNotFoundException(string message) : ApplicationExceptionBase("resource_not_found", message);