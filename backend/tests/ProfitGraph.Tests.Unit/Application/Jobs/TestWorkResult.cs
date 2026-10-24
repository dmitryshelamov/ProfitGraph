using ProfitGraph.Application.Jobs;

namespace ProfitGraph.Tests.Unit.Application.Jobs;

public sealed record TestWorkResult(int Size) : IBackgroundJobResult;