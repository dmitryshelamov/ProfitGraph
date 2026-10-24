using Microsoft.Extensions.Configuration;

using ProfitGraph.Infrastructure.Common;

namespace ProfitGraph.Tests.Unit.Infrastructure.Common;

public sealed class WorkerRolesTests
{
    [Test]
    public void NoConfiguredRoleMeansBoth()
    {
        WorkerRoles.Read(Configuration()).ShouldBe(["jobs", "live"], ignoreOrder: true);
    }

    [Test]
    public void RolesComeFromAListOrACommaSeparatedValue()
    {
        WorkerRoles.Read(Configuration(("Worker:Roles:0", "live"))).ShouldBe(["live"]);
        WorkerRoles.Read(Configuration(("Worker:Roles", "jobs, live"))).ShouldBe(["jobs", "live"], ignoreOrder: true);
    }

    [TestCase("trading")]
    [TestCase("")]
    [TestCase("jobs,")]
    public void UnknownOrEmptyRoleStopsStartup(string roles)
    {
        Should.Throw<InvalidOperationException>(() => WorkerRoles.Read(Configuration(("Worker:Roles", roles))));
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();
    }
}