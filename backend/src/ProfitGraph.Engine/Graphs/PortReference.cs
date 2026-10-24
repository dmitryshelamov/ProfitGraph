namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Ссылка на порт конкретного экземпляра ноды: ID ноды и ключ порта.
///     Используется в соединениях, Wired-привязках и адресации результатов вычисления.
/// </summary>
public sealed record PortReference(string NodeId, string PortKey)
{
    // Порт — ключ словарей значений на каждой свече прогона, поэтому хеш двух строк считается один раз.
    private int _hash;

    private PortReference(PortReference original)
    {
        NodeId = original.NodeId;
        PortKey = original.PortKey;
    }

    public bool Equals(PortReference? other)
    {
        return ReferenceEquals(this, other) ||
            (other is not null && GetHashCode() == other.GetHashCode() &&
                string.Equals(NodeId, other.NodeId, StringComparison.Ordinal) &&
                string.Equals(PortKey, other.PortKey, StringComparison.Ordinal));
    }

    public override int GetHashCode()
    {
        int hash = _hash;

        if (hash == 0)
        {
            hash = HashCode.Combine(NodeId, PortKey);
            _hash = hash == 0 ? 1 : hash;
        }

        return _hash;
    }
}