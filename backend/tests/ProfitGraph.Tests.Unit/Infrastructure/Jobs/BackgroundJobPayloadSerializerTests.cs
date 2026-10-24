using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Infrastructure.Jobs;
using ProfitGraph.Tests.Unit.Application.Jobs;

namespace ProfitGraph.Tests.Unit.Infrastructure.Jobs;

public sealed class BackgroundJobPayloadSerializerTests
{
    private readonly BackgroundJobPayloadSerializer _serializer =
        new BackgroundJobPayloadSerializer([new BackgroundJobRegistration(TestWorkRequest.Kind, typeof(TestWorkRequest), typeof(TestWorkResult))]);

    [Test]
    public void RoundTripsRequestsResultsAndFailures()
    {
        string request = _serializer.SerializeRequest(TestWorkRequest.Kind, new TestWorkRequest(Size: 5));
        string? result = _serializer.SerializeResult(TestWorkRequest.Kind, new TestWorkResult(Size: 5));
        string? failure = BackgroundJobPayloadSerializer.SerializeFailure(new BackgroundJobFailureInternal("code", "message"));

        _serializer.DeserializeRequest(TestWorkRequest.Kind, request).ShouldBeOfType<TestWorkRequest>().ShouldBe(new TestWorkRequest(Size: 5));
        _serializer.DeserializeResult(TestWorkRequest.Kind, result).ShouldBe(new TestWorkResult(Size: 5));
        BackgroundJobPayloadSerializer.DeserializeFailure(failure).ShouldBe(new BackgroundJobFailureInternal("code", "message"));
        request.ShouldNotContain("kind");
    }

    [Test]
    public void AnUnregisteredKindIsAnError()
    {
        var unknown = new BackgroundJobKind("unknown_work");

        Should.Throw<InvalidOperationException>(() => _serializer.SerializeRequest(unknown, new TestWorkRequest(Size: 1)));
        Should.Throw<InvalidOperationException>(() => _serializer.DeserializeRequest(unknown, "{}"));
    }

    [Test]
    public void AKindAcceptsOnlyItsOwnTypes()
    {
        Should.Throw<InvalidOperationException>(() => _serializer.SerializeRequest(TestWorkRequest.Kind, new object()));
    }

    [Test]
    public void AKindIsRegisteredOnce()
    {
        var registration = new BackgroundJobRegistration(TestWorkRequest.Kind, typeof(TestWorkRequest), typeof(TestWorkResult));

        Should.Throw<InvalidOperationException>(() => new BackgroundJobPayloadSerializer([registration, registration]));
    }
}