namespace ProfitGraph.Domain.Optimization;

public enum OptimizationSearchMethod
{
    /// <summary>
    ///     The whole grid when it fits the budget, otherwise a seeded random sample.
    /// </summary>
    Grid,

    Random,

    /// <summary>
    ///     A coarse grid within a third of the budget, then the nearest unvisited neighbors of its best three trials.
    /// </summary>
    CoarseToFine,

    /// <summary>
    ///     Version-one tree-structured Parzen estimator: a seeded random startup, then batches guided by the best trials.
    /// </summary>
    Tpe
}