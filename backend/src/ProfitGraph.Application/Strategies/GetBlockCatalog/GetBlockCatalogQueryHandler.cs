using MediatR;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Application.Strategies.GetBlockCatalog;

internal sealed class GetBlockCatalogQueryHandler : IRequestHandler<GetBlockCatalogQuery, BlockCatalogInternal>
{
    public Task<BlockCatalogInternal> Handle(GetBlockCatalogQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var numeric = new[] { "Integer", "Scalar", "Price", "BaseAmount", "QuoteAmount" };
        BlockCatalogOperandInternal[] pair = [Operand("a", "Первое значение", numeric), Operand("b", "Второе значение", numeric)];
        var catalog = new BlockCatalogInternal(
            [
                Entry("event", "Событие", choices: ["out"]), Entry("condition", "Условие", choices: ["yes", "no"]),
                Entry("order", "Ордер", choices: ["submitted", "filled", "cancelled", "rejected"]),
                Entry("cancel", "Отмена", [Operand("tag", "Метка", ["Tag"])], choices: ["completed", "rejected"])
            ],
            [
                Entry("OnStart", "При старте"), Entry("OnResume", "При возобновлении"), Entry("OnCandleClosed", "Свеча закрылась"),
                Entry("OnOrderFilled", "Ордер исполнен", [Operand("tag", "Метка", ["Tag"], required: false)]),
                Entry("OnOrderCancelled", "Ордер отменён", [Operand("tag", "Метка", ["Tag"], required: false)]),
                Entry("OnOrderRejected", "Ордер отклонён", [Operand("tag", "Метка", ["Tag"], required: false)])
            ],
            [
                Entry("CrossesAbove", "Пересечение вверх", pair, "Boolean"), Entry("CrossesBelow", "Пересечение вниз", pair, "Boolean"),
                Entry("Compare", "Сравнение", pair, "Boolean", ["<", "<=", "=", "!=", ">=", ">"]),
                Entry("PositionFlat", "Позиция закрыта", output: "Boolean"), Entry("NoActiveOrders", "Нет активных ордеров", output: "Boolean"),
                Entry("NoActiveTaggedOrder", "Нет ордера с меткой", [Operand("tag", "Метка", ["Tag"])], "Boolean", ["out", "exists"]),
                Entry("HasBaseBalance", "Есть базовый баланс", [Operand("minQuantity", "Минимальное количество", ["BaseAmount"], required: false)], "Boolean"),
                Entry("PositionFlatPreview", "Остаток позиции", [Operand("quantity", "Количество", ["BaseAmount"])], "Boolean", ["out", "valid"]),
                Entry(
                    "OrderSizePreviewValid",
                    "Размер ордера допустим",
                    [
                        Operand("amount", "Сумма", ["QuoteAmount"], required: false), Operand("quantity", "Количество", ["BaseAmount"], required: false),
                        Operand("price", "Цена", ["Price"], required: false),
                        Operand("quoteLimit", "Предел суммы", ["QuoteAmount"], required: false)
                    ],
                    "Boolean",
                    sides: StrategyBlockSchema.Sides,
                    sizeModes: StrategyBlockSchema.SizeModes,
                    orderKinds: StrategyBlockSchema.OrderKinds)
            ],
            [
                Entry(
                    "Market",
                    "Рыночный",
                    [
                        Operand("amount", "Сумма", ["QuoteAmount"], required: false), Operand("quantity", "Количество", ["BaseAmount"], required: false),
                        Operand("price", "Цена пересчёта", ["Price"], required: false),
                        Operand("tag", "Метка", ["Tag"], required: false)
                    ],
                    sides: StrategyBlockSchema.Sides,
                    sizeModes: StrategyBlockSchema.SizeModes),
                Entry(
                    "Limit",
                    "Лимитный",
                    [
                        Operand("amount", "Сумма", ["QuoteAmount"], required: false), Operand("quantity", "Количество", ["BaseAmount"], required: false),
                        Operand("price", "Цена", ["Price"]), Operand("tag", "Метка", ["Tag"], required: false)
                    ],
                    sides: StrategyBlockSchema.Sides,
                    sizeModes: StrategyBlockSchema.SizeModes)
            ],
            [
                Entry(
                    "constant",
                    "Константа",
                    output: "declared",
                    choices: ["Integer", "Scalar", "Price", "BaseAmount", "QuoteAmount", "Boolean", "Tag", "OrderId", "RejectionReason"]),
                Entry("parameter", "Параметр", output: "declared"),
                Entry("value", "Именованное значение", output: "inferred"),
                Entry("candle", "Поле свечи", output: "field", choices: ["Open", "High", "Low", "Close", "Volume"]),
                Entry("SMA", "Скользящая средняя", [Operand("in", "Значение", numeric), Operand("period", "Период", ["Integer"])], "input"),
                Entry("ATR", "Средний истинный диапазон", [Operand("period", "Период", ["Integer"])], "Price"),
                Entry("EMA", "Экспоненциальная средняя (EMA)", [Operand("in", "Значение", numeric), Operand("period", "Период", ["Integer"])], "input"),
                Entry("RSI", "Индекс относительной силы (RSI)", [Operand("in", "Значение", numeric), Operand("period", "Период", ["Integer"])], "Scalar"),
                Entry(
                    "Bollinger",
                    "Полосы Боллинджера",
                    [Operand("in", "Значение", numeric), Operand("period", "Период", ["Integer"]), Operand("multiplier", "Число отклонений", ["Scalar"])],
                    "field",
                    ["upper", "middle", "lower"]),
                Entry(
                    "MACD",
                    "Схождение-расхождение средних (MACD)",
                    [
                        Operand("in", "Значение", numeric), Operand("fastPeriod", "Быстрый период", ["Integer"]),
                        Operand("slowPeriod", "Медленный период", ["Integer"]), Operand("signalPeriod", "Период сигнальной линии", ["Integer"])
                    ],
                    "field",
                    ["macd", "signal", "histogram"]),
                Entry("previous", "Предыдущее значение", [Operand("in", "Значение", numeric)], "input"),
                Entry("balance", "Баланс", output: "field", choices: ["baseAvailable", "quoteAvailable", "baseReserved", "quoteReserved", "baseTotal", "quoteTotal"]),
                Entry("position", "Позиция", output: "field", choices: ["quantity", "costBasis", "averageCost", "lastBuyPrice", "realizedPnl", "barsHeld"]),
                Entry(
                    "activeOrder",
                    "Активный ордер",
                    [Operand("tag", "Метка", ["Tag"])],
                    "field",
                    ["exists", "order", "orderId", "side", "type", "price", "reservedQuote", "reservedBase"]),
                Entry("arithmetic", "Арифметика", pair, "inferred", ["Add", "Subtract", "Multiply", "Divide", "Min", "Max", "Modulo"])
            ]);

        return Task.FromResult(catalog);
    }

    private static BlockCatalogEntryInternal Entry(
        string key,
        string label,
        IReadOnlyList<BlockCatalogOperandInternal>? operands = null,
        string? output = null,
        IReadOnlyList<string>? choices = null,
        IReadOnlyList<string>? sides = null,
        IReadOnlyList<string>? sizeModes = null,
        IReadOnlyList<string>? orderKinds = null)
    {
        IReadOnlyList<BlockCatalogOperandInternal>? fields = key switch
        {
            "candle" =>
            [
                Field("Open", "Открытие", "Price"), Field("High", "Максимум", "Price"), Field("Low", "Минимум", "Price"), Field("Close", "Закрытие", "Price"),
                Field("Volume", "Объём", "BaseAmount")
            ],
            "balance" =>
            [
                Field("baseAvailable", "Доступно базового актива", "BaseAmount"), Field("baseReserved", "В резерве базового актива", "BaseAmount"),
                Field("baseTotal", "Всего базового актива", "BaseAmount"), Field("quoteAvailable", "Доступно котируемого актива", "QuoteAmount"),
                Field("quoteReserved", "В резерве котируемого актива", "QuoteAmount"), Field("quoteTotal", "Всего котируемого актива", "QuoteAmount")
            ],
            "position" =>
            [
                Field("quantity", "Количество", "BaseAmount"), Field("costBasis", "Стоимость", "QuoteAmount"), Field("averageCost", "Средняя цена", "Price"),
                Field("lastBuyPrice", "Последняя цена покупки", "Price"), Field("realizedPnl", "Реализованный PnL", "QuoteAmount"),
                Field("barsHeld", "Свечей в позиции", "Integer")
            ],
            "Bollinger" => [Field("upper", "Верхняя полоса", "input"), Field("middle", "Средняя линия", "input"), Field("lower", "Нижняя полоса", "input")],
            "MACD" => [Field("macd", "Линия MACD", "input"), Field("signal", "Сигнальная линия", "input"), Field("histogram", "Гистограмма MACD", "input")],
            "activeOrder" =>
            [
                Field("exists", "Существует", "Boolean"), Field("order", "Ордер", "Order"), Field("orderId", "Идентификатор", "OrderId"),
                Field("side", "Сторона", "OrderSide"), Field("type", "Тип", "OrderType"), Field("price", "Цена", "Price"),
                Field("reservedQuote", "Резерв суммы", "QuoteAmount"), Field("reservedBase", "Резерв количества", "BaseAmount")
            ],
            _ => null
        };

        return new BlockCatalogEntryInternal(
            key,
            label,
            operands ?? [],
            output,
            choices,
            fields,
            sides,
            sizeModes,
            orderKinds,
            key is "Market" or "Limit" or "OrderSizePreviewValid" ? new[] { "Reject", "RaiseToExchangeMinimum" } : null);
    }

    private static BlockCatalogOperandInternal Field(string key, string label, string type)
    {
        return new BlockCatalogOperandInternal(key, label, [type], Required: false);
    }

    private static BlockCatalogOperandInternal Operand(string key, string label, IReadOnlyList<string> types, bool required = true)
    {
        return new BlockCatalogOperandInternal(key, label, types, required);
    }
}