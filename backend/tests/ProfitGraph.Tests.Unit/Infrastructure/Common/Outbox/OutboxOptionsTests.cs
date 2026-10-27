using ProfitGraph.Infrastructure.Common.Outbox;

namespace ProfitGraph.Tests.Unit.Infrastructure.Common.Outbox;

public sealed class OutboxOptionsTests
{
    [Test]
    public void DefaultsAreValid()
    {
        new OutboxOptions().IsValid().ShouldBeTrue();
    }

    [Test]
    public void NonPositiveLimitsAreRejected()
    {
        new OutboxOptions { MaxAttempts = 0 }.IsValid().ShouldBeFalse();
        new OutboxOptions { Retention = TimeSpan.Zero }.IsValid().ShouldBeFalse();
        new OutboxOptions { RetentionBatchSize = 0 }.IsValid().ShouldBeFalse();
        new OutboxOptions { MaxRetryDelay = TimeSpan.Zero }.IsValid().ShouldBeFalse();
    }
}