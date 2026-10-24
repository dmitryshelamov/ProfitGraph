using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

internal sealed class ScriptedSynchronousAccount : IGraphAccount
{
    private readonly List<OrderRequest> _submissions = new List<OrderRequest>();
    private readonly List<string> _cancelledTags = new List<string>();

    public TradingAccount Inner { get; }
    public IReadOnlyList<OrderRequest> Submissions => _submissions;
    public IReadOnlyList<string> CancelledTags => _cancelledTags;
    public ExecutionSettings Settings => Inner.Settings;
    public InstrumentRules Rules => Inner.Rules;

    public ScriptedSynchronousAccount(ExecutionSettings settings, InstrumentRules rules)
    {
        Inner = new TradingAccount(settings, rules);
    }

    public AccountSnapshot Capture()
    {
        return Inner.Capture();
    }

    public OrderSubmission Submit(OrderRequest request)
    {
        _submissions.Add(request);

        return Inner.Submit(request);
    }

    public Order? Cancel(long orderId)
    {
        return Inner.Cancel(orderId);
    }

    public Order? CancelByTag(string tag)
    {
        _cancelledTags.Add(tag);

        return Inner.CancelByTag(tag);
    }
}