namespace ProfitGraph.Application.Common.Exceptions;

public sealed class StateConflictException(string message, string code = "state_conflict") : ApplicationExceptionBase(code, message);