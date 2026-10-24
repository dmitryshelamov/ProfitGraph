using System.Text.Json;
using System.Text.Json.Serialization;

using ProfitGraph.Domain.Common;

namespace ProfitGraph.Infrastructure.Common.Outbox;

/// <summary>
///     Names every Domain event by its type name, which is what the outbox stores, and serializes it.
/// </summary>
internal sealed class OutboxEventTypes
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        Converters = { new ValueObjectJsonConverterFactory(), new JsonStringEnumConverter() }
    };

    private readonly Dictionary<string, Type> _byName;

    public OutboxEventTypes()
    {
        Type[] events =
        [
            .. typeof(AggregateRoot).Assembly.GetTypes()
                .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(type))
        ];
        string[] duplicates = [.. events.GroupBy(type => type.Name).Where(group => group.Count() > 1).Select(group => group.Key)];

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"Domain event names must be unique: {string.Join(", ", duplicates)}.");
        }

        _byName = events.ToDictionary(type => type.Name, StringComparer.Ordinal);
    }

    public string NameOf(IDomainEvent domainEvent)
    {
        string name = domainEvent.GetType().Name;

        return _byName.ContainsKey(name) ? name : throw new InvalidOperationException($"{name} is not a Domain event.");
    }

    public string Serialize(IDomainEvent domainEvent)
    {
        return JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Options);
    }

    /// <summary>
    ///     Reads a stored event, or returns null for a type this build does not know.
    /// </summary>
    public IDomainEvent? Deserialize(string name, string payload)
    {
        return _byName.TryGetValue(name, out Type? type) ? (IDomainEvent?)JsonSerializer.Deserialize(payload, type, Options) : null;
    }
}