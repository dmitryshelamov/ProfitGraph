namespace ProfitGraph.Application.Common.Exceptions;

public abstract class ApplicationExceptionBase(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}