namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Детерминированный счётчик идентичностей одного replay; новый run начинает отдельную последовательность.
/// </summary>
public sealed class RunSequence
{
    private long _sequence;

    public long Next()
    {
        return checked(++_sequence);
    }
}