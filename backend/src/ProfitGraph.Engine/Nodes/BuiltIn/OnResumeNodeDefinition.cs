using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Event source for a restored or changed graph engine; the reason is carried by ResumePayload.
/// </summary>
public sealed class OnResumeNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.OnResume;
    public bool EvaluatesDispatch => true;
    public NodeClock? EngineClock => NodeClock.Event;
    public NodeCategory Category => NodeCategory.Signal;
    public int SchemaVersion => 1;
    public IReadOnlyList<PortSpec> Inputs { get; } = Array.Empty<PortSpec>();
    public IReadOnlyList<PortSpec> Outputs { get; } = Array.AsReadOnly(new[] { new PortSpec("out", PortKind.Event, "out", ValueType.Event, EventPayloadType.Resume) });
    public IReadOnlyList<ParamSpec> Params { get; } = Array.Empty<ParamSpec>();

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return context.Occurrence?.Kind == EventKind.Resume
            ? EngineValue.FromEvent(context.Occurrence)
            : EngineValue.Unavailable(ValueType.Event);
    }
}