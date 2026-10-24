namespace ProfitGraph.Application.Common.Exceptions;

public sealed class StateConflictException(string message) : ApplicationExceptionBase("state_conflict", message);