namespace ProfitGraph.Application.Common.Exceptions;

public sealed record InputError(string Code, string Field, string Message);