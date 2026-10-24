using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class PortReferenceTests
{
    [Test]
    public void EqualityAndHashFollowNodeAndPortOrdinally()
    {
        var reference = new PortReference("node", "out");
        var same = new PortReference(new string("node".AsSpan()), new string("out".AsSpan()));

        reference.GetHashCode().ShouldBe(same.GetHashCode());
        reference.ShouldBe(same);
        (reference == same).ShouldBeTrue();
        reference.ShouldNotBe(new PortReference("node", "Out"));
        reference.ShouldNotBe(new PortReference("other", "out"));
        reference.Equals(null).ShouldBeFalse();
        new Dictionary<PortReference, int> { [reference] = 1 }[same].ShouldBe(1);
    }

    [Test]
    public void CopyWithAnotherPortDoesNotReuseTheOriginalHash()
    {
        var reference = new PortReference("node", "out");
        _ = reference.GetHashCode();
        PortReference copy = reference with { PortKey = "value" };

        copy.ShouldBe(new PortReference("node", "value"));
        copy.GetHashCode().ShouldBe(new PortReference("node", "value").GetHashCode());
        copy.ShouldNotBe(reference);
    }
}