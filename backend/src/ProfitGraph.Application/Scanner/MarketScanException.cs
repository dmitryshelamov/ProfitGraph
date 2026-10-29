using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Application.Scanner;

public sealed class MarketScanException(string code, string message) : ApplicationExceptionBase(code, message);