using ProfitGraph.Application.Jobs;

namespace ProfitGraph.Tests.Integration.Jobs;

public sealed record TestJobResult(int Steps) : IBackgroundJobResult;