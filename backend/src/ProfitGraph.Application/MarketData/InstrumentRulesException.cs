using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Application.MarketData;

public sealed class InstrumentRulesException(string code, string message) : ApplicationExceptionBase(code, message);