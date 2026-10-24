using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Nodes;

public sealed class UnitAlgebraTests
{
    [TestCase("Multiply", ValueType.Integer, ValueType.Scalar, ValueType.Scalar)]
    [TestCase("Multiply", ValueType.Scalar, ValueType.Integer, ValueType.Scalar)]
    [TestCase("Divide", ValueType.Integer, ValueType.Scalar, ValueType.Scalar)]
    [TestCase("Multiply", ValueType.Integer, ValueType.Integer, ValueType.Integer)]
    [TestCase("Add", ValueType.Integer, ValueType.Integer, ValueType.Integer)]
    [TestCase("Subtract", ValueType.Integer, ValueType.Integer, ValueType.Integer)]
    [TestCase("Min", ValueType.Integer, ValueType.Integer, ValueType.Integer)]
    [TestCase("Divide", ValueType.Integer, ValueType.Integer, ValueType.Scalar)]
    [TestCase("Add", ValueType.Price, ValueType.Price, ValueType.Price)]
    [TestCase("Subtract", ValueType.QuoteAmount, ValueType.QuoteAmount, ValueType.QuoteAmount)]
    [TestCase("Min", ValueType.BaseAmount, ValueType.BaseAmount, ValueType.BaseAmount)]
    [TestCase("Max", ValueType.Integer, ValueType.Integer, ValueType.Integer)]
    [TestCase("Modulo", ValueType.Integer, ValueType.Integer, ValueType.Integer)]
    [TestCase("Modulo", ValueType.Price, ValueType.Price, ValueType.Price)]
    [TestCase("Multiply", ValueType.Price, ValueType.Scalar, ValueType.Price)]
    [TestCase("Multiply", ValueType.Scalar, ValueType.BaseAmount, ValueType.BaseAmount)]
    [TestCase("Multiply", ValueType.Price, ValueType.BaseAmount, ValueType.QuoteAmount)]
    [TestCase("Multiply", ValueType.BaseAmount, ValueType.Price, ValueType.QuoteAmount)]
    [TestCase("Divide", ValueType.QuoteAmount, ValueType.Price, ValueType.BaseAmount)]
    [TestCase("Divide", ValueType.QuoteAmount, ValueType.BaseAmount, ValueType.Price)]
    [TestCase("Divide", ValueType.Price, ValueType.Price, ValueType.Scalar)]
    [TestCase("Divide", ValueType.Price, ValueType.Scalar, ValueType.Price)]
    public void ValidOperationsPreserveUnits(string operation, ValueType left, ValueType right, ValueType expected)
    {
        UnitAlgebra.Resolve(operation, left, right).ShouldBe(expected);
    }

    [TestCase("Add", ValueType.Price, ValueType.QuoteAmount)]
    [TestCase("Subtract", ValueType.BaseAmount, ValueType.Scalar)]
    [TestCase("Min", ValueType.Price, ValueType.Scalar)]
    [TestCase("Multiply", ValueType.Price, ValueType.Price)]
    [TestCase("Multiply", ValueType.Integer, ValueType.QuoteAmount)]
    [TestCase("Divide", ValueType.BaseAmount, ValueType.Price)]
    [TestCase("Divide", ValueType.Scalar, ValueType.Price)]
    [TestCase("Modulo", ValueType.Integer, ValueType.Scalar)]
    [TestCase("Add", ValueType.Boolean, ValueType.Boolean)]
    [TestCase("Add", ValueType.OrderId, ValueType.OrderId)]
    [TestCase("Unknown", ValueType.Scalar, ValueType.Scalar)]
    public void InvalidCombinationsHaveNoResultType(string operation, ValueType left, ValueType right)
    {
        UnitAlgebra.Resolve(operation, left, right).ShouldBeNull();
    }
}