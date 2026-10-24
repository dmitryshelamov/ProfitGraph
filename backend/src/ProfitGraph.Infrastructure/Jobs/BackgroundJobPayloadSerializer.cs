using System.Text.Json;
using System.Text.Json.Serialization;

using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Infrastructure.Jobs;

/// <summary>
///     Maps stored job kinds to request and result types. An unregistered kind or type is an error rather than a guess.
/// </summary>
internal sealed class BackgroundJobPayloadSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private readonly Dictionary<BackgroundJobKind, BackgroundJobRegistration> _byKind;

    public BackgroundJobPayloadSerializer(IEnumerable<BackgroundJobRegistration> registrations)
    {
        _byKind = new Dictionary<BackgroundJobKind, BackgroundJobRegistration>();

        foreach (BackgroundJobRegistration registration in registrations)
        {
            if (!_byKind.TryAdd(registration.Kind, registration))
            {
                throw new InvalidOperationException($"Background job kind '{registration.Kind}' is registered twice.");
            }
        }
    }

    public string SerializeRequest(BackgroundJobKind kind, object request)
    {
        BackgroundJobRegistration registration = Find(kind);

        if (request.GetType() != registration.RequestType)
        {
            throw new InvalidOperationException($"Background job kind '{kind}' expects {registration.RequestType.Name}, not {request.GetType().Name}.");
        }

        return JsonSerializer.Serialize(request, registration.RequestType, Options);
    }

    public IBackgroundJobRequest DeserializeRequest(BackgroundJobKind kind, string json)
    {
        return (IBackgroundJobRequest)(JsonSerializer.Deserialize(json, Find(kind).RequestType, Options)
            ?? throw new InvalidOperationException($"Background job '{kind}' has an empty request."));
    }

    public string? SerializeResult(BackgroundJobKind kind, IBackgroundJobResult? result)
    {
        if (result is null)
        {
            return null;
        }

        BackgroundJobRegistration registration = Find(kind);

        return result.GetType() == registration.ResultType
            ? JsonSerializer.Serialize(result, registration.ResultType, Options)
            : throw new InvalidOperationException($"Background job kind '{kind}' returns {registration.ResultType.Name}, not {result.GetType().Name}.");
    }

    public IBackgroundJobResult? DeserializeResult(BackgroundJobKind kind, string? json)
    {
        return json is null ? null : (IBackgroundJobResult?)JsonSerializer.Deserialize(json, Find(kind).ResultType, Options);
    }

    public static string? SerializeFailure(BackgroundJobFailureInternal? failure)
    {
        return failure is null ? null : JsonSerializer.Serialize(failure, Options);
    }

    public static BackgroundJobFailureInternal? DeserializeFailure(string? json)
    {
        return json is null ? null : JsonSerializer.Deserialize<BackgroundJobFailureInternal>(json, Options);
    }

    private BackgroundJobRegistration Find(BackgroundJobKind kind)
    {
        return _byKind.TryGetValue(kind, out BackgroundJobRegistration? registration)
            ? registration
            : throw new InvalidOperationException($"Background job kind '{kind}' is not registered.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}