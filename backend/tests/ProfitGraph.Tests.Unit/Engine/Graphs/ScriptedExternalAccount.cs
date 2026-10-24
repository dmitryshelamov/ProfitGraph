using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

internal sealed class ScriptedExternalAccount : IExternallyExecutedAccount
{
    private readonly ExternallyExecutedAccount _inner;
    private readonly List<OrderRequest> _submissions = new List<OrderRequest>();

    public IReadOnlyList<OrderRequest> Submissions => _submissions;
    public int EntriesWhilePositionOpen { get; private set; }
    public bool DuplicateTagsObserved { get; private set; }
    public ExecutionSettings Settings => _inner.Settings;
    public InstrumentRules Rules => _inner.Rules;

    public ScriptedExternalAccount(ExecutionSettings settings, InstrumentRules rules)
    {
        _inner = new ExternallyExecutedAccount(settings, rules);
    }

    public AccountSnapshot Capture()
    {
        return _inner.Capture();
    }

    public OrderSubmission Submit(OrderRequest request)
    {
        _submissions.Add(request);

        if (request.Tag == "ladder.entry" && Capture().BaseTotal > 0m)
        {
            EntriesWhilePositionOpen++;
        }

        OrderSubmission result = _inner.Submit(request);
        ObserveTags();

        return result;
    }

    public Order? Cancel(long orderId)
    {
        return _inner.Cancel(orderId);
    }

    public Order? CancelByTag(string tag)
    {
        return _inner.CancelByTag(tag);
    }

    public ExternalCancelRequest RequestCancel(long orderId)
    {
        return _inner.RequestCancel(orderId);
    }

    public ExternalCancelRequest RequestCancelByTag(string tag)
    {
        return _inner.RequestCancelByTag(tag);
    }

    public ExternalOrderUpdateResult ApplyExternalUpdate(ExternalOrderUpdate update, long rejectionAttemptId)
    {
        ExternalOrderUpdateResult result = _inner.ApplyExternalUpdate(update, rejectionAttemptId);
        ObserveTags();

        return result;
    }

    private void ObserveTags()
    {
        string[] tags = _inner.Capture().Orders.Where(order => order.Tag is not null).Select(order => order.Tag!).ToArray();
        DuplicateTagsObserved |= tags.Distinct(StringComparer.Ordinal).Count() != tags.Length;
    }
}