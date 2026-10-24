using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Tests.Unit.Engine.Nodes;

public sealed class ResolvedParamsTests
{
    [Test]
    public void ReadsTypedNumberIntegerAndText()
    {
        var parameters = new ResolvedParams(
            new Dictionary<string, ParamLiteral>
            {
                ["period"] = new NumberParamLiteral(14),
                ["symbol"] = new TextParamLiteral("BTC/USDT")
            });

        parameters.ShouldSatisfyAllConditions(
            () => parameters.GetNumber("period").ShouldBe(14d),
            () => parameters.GetInt("period").ShouldBe(14),
            () => parameters.GetString("symbol").ShouldBe("BTC/USDT"));
    }

    [Test]
    public void NumericTextIsNotImplicitlyConvertedToNumber()
    {
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral> { ["period"] = new TextParamLiteral("14") });

        var exception = Should.Throw<ParameterResolutionException>(() => parameters.GetNumber("period"));
        exception!.ParamKey.ShouldBe("period");
        Should.Throw<ParameterResolutionException>(() => parameters.GetInt("period"));
    }

    [Test]
    public void NumberCannotBeReadAsText()
    {
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral> { ["value"] = new NumberParamLiteral(1) });

        var exception = Should.Throw<ParameterResolutionException>(() => parameters.GetString("value"));
        exception!.ParamKey.ShouldBe("value");
    }

    [Test]
    public void MissingParameterProducesDomainError()
    {
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral>());

        Should.Throw<ParameterResolutionException>(() => parameters.GetNumber("missing"));
        Should.Throw<ParameterResolutionException>(() => parameters.GetString("missing"));
    }

    [TestCase(1.5)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(2147483648d)]
    [TestCase(-2147483649d)]
    public void InvalidIntegerProducesDomainError(double value)
    {
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral> { ["period"] = new NumberParamLiteral(value) });

        Should.Throw<ParameterResolutionException>(() => parameters.GetInt("period"));
    }
}