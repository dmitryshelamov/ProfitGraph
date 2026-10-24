using System.Reflection;

using ProfitGraph.Domain.Common;

namespace ProfitGraph.Tests.Unit.Architecture;

/// <summary>
///     Domain entities and read models expose identifiers as typed value objects (StrategyId, UserId), never as raw Guid;
///     only a value object's own <c>Value</c> wraps the Guid.
/// </summary>
public sealed class DomainValueObjectTests
{
    [Test]
    public void DomainTypesDoNotExposeRawGuids()
    {
        string[] offenders =
        [
            .. typeof(AggregateRoot).Assembly.GetExportedTypes()
                .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(property => property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?))
                    .Where(property => property.Name != "Value")
                    .Select(property => $"{type.Name}.{property.Name}"))
        ];

        offenders.ShouldBeEmpty("Domain identifiers are typed value objects.");
    }
}