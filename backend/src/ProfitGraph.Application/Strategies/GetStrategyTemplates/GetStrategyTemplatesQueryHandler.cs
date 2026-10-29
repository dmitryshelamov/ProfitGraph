using MediatR;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Application.Strategies.Templates;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Application.Strategies.GetStrategyTemplates;

internal sealed class GetStrategyTemplatesQueryHandler : IRequestHandler<GetStrategyTemplatesQuery, IReadOnlyList<StrategyTemplateInternal>>
{
    public Task<IReadOnlyList<StrategyTemplateInternal>> Handle(GetStrategyTemplatesQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StrategyDefinition sma = SmaStrategyBlockFactory.Create();

        // The catalog's Ladder is the Ladder with resume and the manual-fill reaction under its own kind (ADR 0040), so a strategy made
        // from it restores its orders after a start, a reconciliation and an order the venue cancelled.
        StrategyDefinition resume = LadderResumeStrategyBlockFactory.Create();
        var ladder = new StrategyDefinition(resume.Parameters, resume.Values, resume.Blocks, resume.Wires, StrategyTemplateKind.Ladder);
        StrategyDefinition profitFox = ProfitFoxLadderStrategyBlockFactory.Create();
        StrategyDefinition ladderSma = LadderSmaStrategyBlockFactory.Create();
        StrategyDefinition capitulationDip = CapitulationDipStrategyBlockFactory.Create();
        StrategyDefinition volumeMomentum = VolumeMomentumStrategyBlockFactory.Create();
        StrategyDefinition ladderPlus = LadderPlusStrategyBlockFactory.Create();
        StrategyDefinition dca = ProfitFoxDcaStrategyBlockFactory.Create();
        StrategyDefinition grid = GridStrategyBlockFactory.Create();
        StrategyDefinition emaCrossover = EmaCrossoverStrategyBlockFactory.Create();
        StrategyDefinition macdTrend = MacdTrendStrategyBlockFactory.Create();
        StrategyDefinition rsiReversion = RsiReversionStrategyBlockFactory.Create();
        StrategyDefinition bollingerReversion = BollingerReversionStrategyBlockFactory.Create();
        IReadOnlyList<StrategyTemplateInternal> templates =
        [
            new StrategyTemplateInternal(
                sma.TemplateKind!.Value,
                "SMA Crossover",
                "Покупка и продажа на пересечении двух скользящих средних.",
                sma.ToInternal(),
                StrategyTemplateLayouts.Sma().ToInternal()),
            new StrategyTemplateInternal(
                ladder.TemplateKind!.Value,
                "Ladder",
                "Вход, усреднение и выход лимитными ордерами: восстановление после возобновления, перестройка при ручной заявке.",
                ladder.ToInternal(),
                StrategyTemplateLayouts.Ladder().ToInternal()),
            new StrategyTemplateInternal(
                StrategyTemplateKind.LadderProfitFox,
                "Ladder (как в Profit Fox)",
                "Ladder с возобновлением и увеличением покупок до минимального notional биржи в пределах лимитов.",
                profitFox.ToInternal(),
                StrategyTemplateLayouts.Ladder().ToInternal()),
            new StrategyTemplateInternal(
                ladderSma.TemplateKind!.Value,
                "Ladder SMA",
                "Ladder с усреднением от SMA на закрытии свечи и восстановлением после возобновления.",
                ladderSma.ToInternal(),
                StrategyTemplateLayouts.Ladder().ToInternal()),
            new StrategyTemplateInternal(
                capitulationDip.TemplateKind!.Value,
                "Откуп капитуляции",
                "Покупка после резкого падения на повышенном объёме; выход по цели или числу свечей в позиции.",
                capitulationDip.ToInternal(),
                StrategyTemplateLayouts.CapitulationDip().ToInternal()),
            new StrategyTemplateInternal(
                volumeMomentum.TemplateKind!.Value,
                "Импульс на объёме",
                "Покупка после сильной растущей свечи на повышенном объёме; выход по цели, стопу или числу свечей в позиции.",
                volumeMomentum.ToInternal(),
                StrategyTemplateLayouts.VolumeMomentum().ToInternal()),
            new StrategyTemplateInternal(
                ladderPlus.TemplateKind!.Value,
                "Ladder+",
                "Ladder как в Profit Fox со стоп-лоссом от средней цены и остановкой новых циклов после цели по прибыли.",
                ladderPlus.ToInternal(),
                StrategyTemplateLayouts.Ladder().ToInternal()),
            new StrategyTemplateInternal(
                dca.TemplateKind!.Value,
                "DCA (как в Profit Fox)",
                "Покупка по рынку на старте и затем каждые N закрытых свечей, пока позиция не достигнет лимита; без продаж.",
                dca.ToInternal(),
                StrategyTemplateLayouts.Ladder().ToInternal()),
            new StrategyTemplateInternal(
                grid.TemplateKind!.Value,
                "Сетка",
                "Лимитные покупки ступенями ниже цены старта, у каждой ступени свой лот и своя продажа с шагом прибыли; спот, только long.",
                grid.ToInternal(),
                StrategyTemplateLayouts.Ladder().ToInternal()),
            new StrategyTemplateInternal(
                emaCrossover.TemplateKind!.Value,
                "EMA Crossover",
                "Покупка, когда быстрая EMA пересекает медленную вверх; выход на обратном пересечении или по стопу.",
                emaCrossover.ToInternal(),
                StrategyTemplateLayouts.IndicatorSignal().ToInternal()),
            new StrategyTemplateInternal(
                macdTrend.TemplateKind!.Value,
                "MACD по тренду",
                "Покупка, когда линия MACD пересекает сигнальную вверх при цене выше EMA тренда; выход на обратном пересечении или по стопу.",
                macdTrend.ToInternal(),
                StrategyTemplateLayouts.IndicatorSignal().ToInternal()),
            new StrategyTemplateInternal(
                rsiReversion.TemplateKind!.Value,
                "Отскок RSI",
                "Покупка, когда RSI возвращается выше уровня перепроданности; выход при RSI на уровне выхода или по стопу.",
                rsiReversion.ToInternal(),
                StrategyTemplateLayouts.IndicatorSignal().ToInternal()),
            new StrategyTemplateInternal(
                bollingerReversion.TemplateKind!.Value,
                "Возврат от полосы Боллинджера",
                "Покупка после закрытия ниже нижней полосы Боллинджера; выход при возврате к средней линии или по стопу.",
                bollingerReversion.ToInternal(),
                StrategyTemplateLayouts.IndicatorSignal().ToInternal())
        ];

        return Task.FromResult(templates);
    }
}