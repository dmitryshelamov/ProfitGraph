namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Валюта удержания комиссии: покупка удерживает базовый актив, продажа — котируемый (ADR 0012).
/// </summary>
public enum FeeAsset
{
    Base,
    Quote
}