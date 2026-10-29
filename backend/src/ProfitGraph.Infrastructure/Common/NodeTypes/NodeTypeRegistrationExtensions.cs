using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Common.Ports;
using ProfitGraph.Engine.Graphs.Composites;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;

namespace ProfitGraph.Infrastructure.Common.NodeTypes;

/// <summary>
///     Регистрирует каталог и встроенные определения нод как singleton.
///     Состояние вычислений передаётся извне.
/// </summary>
public static class NodeTypeRegistrationExtensions
{
    public static IServiceCollection AddNodeTypes(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<INodeDefinition, DataSourceNodeDefinition>();
        services.AddSingleton<INodeDefinition, SmaNodeDefinition>();
        services.AddSingleton<INodeDefinition, AtrNodeDefinition>();
        services.AddSingleton<INodeDefinition, EmaNodeDefinition>();
        services.AddSingleton<INodeDefinition, RsiNodeDefinition>();
        services.AddSingleton<INodeDefinition, StandardDeviationNodeDefinition>();
        services.AddSingleton<INodeDefinition, ConstantNodeDefinition>();
        services.AddSingleton<INodeDefinition, ParameterNodeDefinition>();
        services.AddSingleton<INodeDefinition, CandleFieldNodeDefinition>();
        services.AddSingleton<INodeDefinition, PreviousCandleValueNodeDefinition>();
        services.AddSingleton<INodeDefinition, ArithmeticNodeDefinition>();
        services.AddSingleton<INodeDefinition, CompareNodeDefinition>();
        services.AddSingleton<INodeDefinition, AndNodeDefinition>();
        services.AddSingleton<INodeDefinition, OrNodeDefinition>();
        services.AddSingleton<INodeDefinition, NotNodeDefinition>();
        services.AddSingleton<INodeDefinition, CrossesAboveNodeDefinition>();
        services.AddSingleton<INodeDefinition, CrossesBelowNodeDefinition>();
        services.AddSingleton<INodeDefinition, IsAvailableNodeDefinition>();
        services.AddSingleton<INodeDefinition, OnStartNodeDefinition>();
        services.AddSingleton<INodeDefinition, OnResumeNodeDefinition>();
        services.AddSingleton<INodeDefinition, OnCandleClosedNodeDefinition>();
        services.AddSingleton<INodeDefinition, MergeEventsNodeDefinition>();
        services.AddSingleton<INodeDefinition, FilterEventNodeDefinition>();
        services.AddSingleton<INodeDefinition, DecisionNodeDefinition>();

        services.AddSingleton<INodeDefinition, BalanceNodeDefinition>();
        services.AddSingleton<INodeDefinition, PositionNodeDefinition>();
        services.AddSingleton<INodeDefinition, ActiveOrderNodeDefinition>();
        services.AddSingleton<INodeDefinition, HasActiveOrdersNodeDefinition>();
        services.AddSingleton<INodeDefinition, OrderSizePreviewNodeDefinition>();
        services.AddSingleton<INodeDefinition, OnOrderFilledNodeDefinition>();
        services.AddSingleton<INodeDefinition, OnOrderCancelledNodeDefinition>();
        services.AddSingleton<INodeDefinition, OnOrderRejectedNodeDefinition>();
        services.AddSingleton<INodeDefinition, EventFieldsNodeDefinition>();
        services.AddSingleton<INodeDefinition, MarketBuyNodeDefinition>();
        services.AddSingleton<INodeDefinition, LimitBuyNodeDefinition>();
        services.AddSingleton<INodeDefinition, MarketSellNodeDefinition>();
        services.AddSingleton<INodeDefinition, LimitSellNodeDefinition>();
        services.AddSingleton<INodeDefinition, CancelOrderNodeDefinition>();

        foreach (CompositeNodeDefinition definition in BuiltInComposites.Definitions)
        {
            services.AddSingleton<INodeDefinition>(definition);
        }

        services.AddSingleton<INodeTypeRegistry, NodeTypeRegistry>();

        return services;
    }
}