using System.Collections.ObjectModel;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Frozen pre-action dispatch state and resolved request set, retained on terminal failures.
/// </summary>
public sealed class DispatchObservation
{
    public EventOccurrence Occurrence { get; }
    public AccountSnapshot Account { get; }
    public IReadOnlyDictionary<PortReference, EngineValue> Outputs { get; }
    public IReadOnlyList<ActionRequest> Requests { get; }

    public DispatchObservation(
        EventOccurrence occurrence,
        AccountSnapshot account,
        IReadOnlyDictionary<PortReference, EngineValue> outputs,
        IEnumerable<ActionRequest> requests)
    {
        Occurrence = occurrence;
        Account = account;
        Outputs = new ReadOnlyDictionary<PortReference, EngineValue>(new Dictionary<PortReference, EngineValue>(outputs));
        Requests = Array.AsReadOnly(requests.ToArray());
    }
}