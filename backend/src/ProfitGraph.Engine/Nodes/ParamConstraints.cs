namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Общие проверки диапазона и шага числового параметра по его схеме.
///     Используются при проверке графа и выполнении нод, не связывая интерпретатор с валидатором графа.
/// </summary>
internal static class ParamConstraints
{
    internal static bool IsInRange(double value, ParamSpec spec)
    {
        return double.IsFinite(value) && (!spec.MinValue.HasValue || value >= spec.MinValue.Value) &&
            (!spec.MaxValue.HasValue || value <= spec.MaxValue.Value) &&
            (!spec.Step.HasValue || IsMultiple(value - (spec.MinValue ?? 0), spec.Step.Value));
    }

    internal static bool IsMultiple(double value, double step)
    {
        double ratio = value / step;

        return double.IsFinite(ratio) && Math.Abs(ratio - Math.Round(ratio)) <= 1e-8;
    }
}