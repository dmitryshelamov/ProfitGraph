using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Confirmations;
using ProfitGraph.Venues.Steam.Orders;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Mobile confirmations of buys against the mock: signed for Steam's time, only the matching confirmation approved, and
///     never approved twice (ADR 0022).
/// </summary>
public sealed class SteamConfirmationsTests
{
    private const string Item = "Dreams & Nightmares Case";
    private const string ApprovePath = "/mobileconf/ajaxop";
    private const string ListPath = "/mobileconf/getlist";
    private const string CreatePath = "/market/createbuyorder/";

    private static readonly Guid Bot = Guid.NewGuid();

    private SteamSessionHarness _harness = null!;
    private InMemorySteamOrderJournal _journal = null!;
    private SteamOrderUpdates _updates = null!;
    private RecordingSteamOrderWaiting _waiting = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SteamSessionHarness
        {
            Options = new SteamOptions
            {
                Enabled = true,
                CommunityBaseAddress = MockSteam.Community,
                LoginBaseAddress = MockSteam.Login,
                ApiBaseAddress = MockSteam.Api,
                WriteSpacing = TimeSpan.Zero
            }
        };
        _harness.Clock.SetUtcNow(_harness.Steam.ServerTime);
        _harness.Steam.BuysNeedConfirmation = true;
        _journal = new InMemorySteamOrderJournal();
        _updates = new SteamOrderUpdates();
        _waiting = new RecordingSteamOrderWaiting();
    }

    [Test]
    public void SignsForSteamsTimeWithTheIdentitySecret()
    {
        DateTimeOffset at = DateTimeOffset.FromUnixTimeSeconds(1792929600);

        SteamConfirmationKey.Sign(_harness.Steam.IdentitySecret, at, "conf").ShouldBe("JcZ9CzwycFMRWaVs+S8L5R6CsbI=");
        SteamConfirmationKey.Sign(_harness.Steam.IdentitySecret, at, "accept").ShouldBe("uSP1hlrLCzCWmdXjnWzDqIBcCkQ=");
        Should.Throw<ArgumentException>(() => SteamConfirmationKey.Sign(_harness.Steam.IdentitySecret, at, "details"));
    }

    [Test]
    public async Task ABuyIsApprovedOnceThenFinalizedWithItsCreator()
    {
        (SteamBuyOrders buys, SteamConfirmations confirmations) = Create();
        await Submit(buys);
        string creator = (await Entry()).ConfirmationCreator!;

        (await Advance(confirmations)).ShouldBeTrue();
        (await Advance(confirmations)).ShouldBeFalse();

        _harness.Steam.Count(ApprovePath).ShouldBe(1);
        _harness.Steam.Count(ListPath).ShouldBe(1);
        _harness.Steam.Count(CreatePath).ShouldBe(2);
        _harness.Steam.LastBuyForm["confirmation"].ShouldBe(creator);
        SteamOrderJournalEntry entry = await Entry();
        entry.State.ShouldBe(SteamOrderJournalState.Active);
        entry.SteamOrderId.ShouldBe("9001");
        (await Published()).ShouldBe([ExternalOrderUpdate.AcceptLocally(orderId: 1), ExternalOrderUpdate.Accept(orderId: 1)]);
    }

    [Test]
    public async Task AForeignConfirmationIsNeverTouched()
    {
        (SteamBuyOrders buys, SteamConfirmations confirmations) = Create();
        await Submit(buys);
        var foreign = new MockSteamConfirmation("4001", "4002", Type: 3, "4003");
        var sameCreatorOtherType = new MockSteamConfirmation("4011", "4012", Type: 3, (await Entry()).ConfirmationCreator!);
        _harness.Steam.Confirmations.AddRange([foreign, sameCreatorOtherType]);

        await Advance(confirmations);

        _harness.Steam.Confirmations.ShouldBe([foreign, sameCreatorOtherType]);
        _harness.Steam.Count(ApprovePath).ShouldBe(1);
    }

    [Test]
    public async Task AConfirmationNotListedYetIsWaitedFor()
    {
        (SteamBuyOrders buys, SteamConfirmations confirmations) = Create();
        await Submit(buys);
        MockSteamConfirmation pending = _harness.Steam.Confirmations.Single();
        _harness.Steam.Confirmations.Clear();

        (await Advance(confirmations)).ShouldBeTrue();
        _harness.Steam.Confirmations.Add(pending);
        (await Advance(confirmations)).ShouldBeTrue();

        _harness.Steam.Count(ApprovePath).ShouldBe(1);
        _harness.Steam.Count(CreatePath).ShouldBe(2);
        (await Entry()).State.ShouldBe(SteamOrderJournalState.Active);
    }

    [Test]
    public async Task ARestartDuringTheApprovalNeverApprovesAgain()
    {
        (SteamBuyOrders buys, SteamConfirmations confirmations) = Create();
        await Submit(buys);
        _journal.Put(await Entry() with { State = SteamOrderJournalState.Approving });

        (await Advance(confirmations)).ShouldBeFalse();

        _harness.Steam.Count(ApprovePath).ShouldBe(0);
        _harness.Steam.Count(CreatePath).ShouldBe(1);
        (await Entry()).State.ShouldBe(SteamOrderJournalState.Approving);
    }

    [Test]
    public async Task ARestartAfterTheApprovalOnlyFinalizes()
    {
        (SteamBuyOrders buys, SteamConfirmations confirmations) = Create();
        await Submit(buys);
        MockSteamConfirmation pending = _harness.Steam.Confirmations.Single();
        _harness.Steam.Confirmations.Remove(pending);
        _harness.Steam.Approved.Add(pending.CreatorId);
        _journal.Put(await Entry() with { State = SteamOrderJournalState.Approved });

        (await Advance(confirmations)).ShouldBeFalse();

        _harness.Steam.Count(ApprovePath).ShouldBe(0);
        _harness.Steam.Count(ListPath).ShouldBe(0);
        (await Entry()).State.ShouldBe(SteamOrderJournalState.Active);
    }

    [Test]
    public async Task AnUnclearApprovalLeavesTheBuyUnknown()
    {
        (SteamBuyOrders buys, SteamConfirmations confirmations) = Create();
        await Submit(buys);
        _harness.Steam.Override = request => request.RequestUri!.AbsolutePath == ApprovePath ? new HttpResponseMessage(HttpStatusCode.BadGateway) : null;

        await Advance(confirmations);
        _harness.Steam.Override = null;
        await Advance(confirmations);

        _harness.Steam.Count(ApprovePath).ShouldBe(1);
        _harness.Steam.Count(CreatePath).ShouldBe(1);
        (await Entry()).State.ShouldBe(SteamOrderJournalState.Unknown);
    }

    [Test]
    public async Task AFinalizeWithoutTheNewBuyOrderIsUnknown()
    {
        (SteamBuyOrders buys, SteamConfirmations confirmations) = Create();
        await Submit(buys);
        _harness.Steam.Override = request => request.RequestUri!.AbsolutePath == ApprovePath
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content: """{"success":true}""", Encoding.UTF8, "application/json")
            }
            : null;

        await Advance(confirmations);

        _harness.Steam.Count(CreatePath).ShouldBe(2);
        (await Entry()).State.ShouldBe(SteamOrderJournalState.Unknown);
    }

    [Test]
    public async Task ABuyAwaitingItsConfirmationCancelsLocallyAndIsNeverApproved()
    {
        (SteamBuyOrders buys, SteamConfirmations confirmations) = Create();
        await Submit(buys);

        await buys.Cancel(Account(), "pg-1", CancellationToken.None);
        (await Advance(confirmations)).ShouldBeFalse();

        _harness.Steam.Count(ApprovePath).ShouldBe(0);
        (await Entry()).Outcome.ShouldBe(SteamOrderOutcome.Cancelled);
    }

    private static SteamBotAccount Account()
    {
        SteamInstrument.TryParse($"KZT:730:{Item}", out SteamInstrument? instrument).ShouldBeTrue();

        return new SteamBotAccount(Bot, SteamSessionHarness.Owner, SteamSessionHarness.Credential, instrument);
    }

    private (SteamBuyOrders Buys, SteamConfirmations Confirmations) Create()
    {
        IOptions<SteamOptions> options = Options.Create(_harness.Options);
        var buys = new SteamBuyOrders(
            _journal,
            _harness.Sessions(),
            new SteamBuyOrderClient(options),
            _updates,
            new FakeSteamBotAccounts(Account),
            _waiting,
            _harness.Clock);
        IServiceScopeFactory scopes = _harness.Scopes();

        return (buys, new SteamConfirmations(
            _journal,
            _harness.Sessions(),
            new SteamConfirmationClient(scopes),
            buys,
            new SteamSellSettlement(_journal, _updates, _waiting, _harness.Clock),
            _harness.Clock));
    }

    private async Task Submit(SteamBuyOrders buys)
    {
        var order = new OrderSnapshot(
            Id: 1,
            Tag: null,
            OrderSide.Buy,
            OrderType.Limit,
            Quantity: 1m,
            LimitPrice: 500m,
            QuoteBudget: null,
            Reservation: 500m,
            "node",
            EligibleFromCandleIndex: 0,
            OrderStatus.Active);

        (await buys.Submit(Account(), "pg-1", order, _ => Task.CompletedTask, CancellationToken.None)).ShouldBeNull();
        (await Entry()).State.ShouldBe(SteamOrderJournalState.AwaitingConfirmation);
    }

    private Task<bool> Advance(SteamConfirmations confirmations)
    {
        return confirmations.Advance(SteamSessionHarness.Owner, SteamSessionHarness.Credential, CancellationToken.None);
    }

    private async Task<SteamOrderJournalEntry> Entry()
    {
        return (await _journal.Get("pg-1", CancellationToken.None)).ShouldNotBeNull();
    }

    private async Task<IReadOnlyList<ExternalOrderUpdate>> Published()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var seen = new List<ExternalOrderUpdate>();

        try
        {
            await foreach (ExternalOrderUpdate update in _updates.Read(Bot, stop.Token))
            {
                seen.Add(update);
            }
        }
        catch (OperationCanceledException)
        {
            // Everything published so far has been read.
        }

        return seen;
    }
}