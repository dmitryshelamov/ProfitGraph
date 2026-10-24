namespace ProfitGraph.Domain.Optimization;

public enum OptimizationSearchMethod
{
    /// <summary>
    ///     The whole grid when it fits the budget, otherwise a seeded random sample.
    /// </summary>
    Grid,

    Random
}