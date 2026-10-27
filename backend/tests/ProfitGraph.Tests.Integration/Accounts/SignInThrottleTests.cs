using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Http;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Accounts;

/// <summary>
///     Sign-in lockout after consecutive failures (ADR 0043): only failures count, the lockout doubles up to a cap, a
///     successful sign-in clears it, and a client address that signed in to the login before keeps its own budget.
/// </summary>
public sealed class SignInThrottleTests
{
    private const string WrongPassword = "wrong-password-123";
    private readonly TestClock _clock = new TestClock();
    private ProfitGraphApiFactory _factory = null!;
    private int _addresses;

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = new ProfitGraphApiFactory(_clock);
        _ = _factory.Server;
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task FailuresLockTheLoginOut_AndEachLockoutDoublesUpToTheCap()
    {
        UserInternal user = await AccountClient.CreateAccount(_factory, UserRole.User);
        string address = NewAddress();
        var statuses = new List<int>();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            statuses.Add((await Attempt(user.Login.ToUpperInvariant(), WrongPassword, address)).StatusCode);
        }

        HttpResponse locked = await Attempt(user.Login, AccountClient.Password, address);
        var lockouts = new List<string?> { locked.Headers.RetryAfter };

        for (var lockout = 0; lockout < 6; lockout++)
        {
            _clock.Now += TimeSpan.FromSeconds(int.Parse(lockouts[^1]!));
            statuses.Add((await Attempt(user.Login, WrongPassword, address)).StatusCode);
            lockouts.Add((await Attempt(user.Login, WrongPassword, address)).Headers.RetryAfter);
        }

        statuses.ShouldAllBe(status => status == StatusCodes.Status401Unauthorized);
        locked.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        lockouts.ShouldBe(["30", "60", "120", "240", "480", "900", "900"]);
    }

    [Test]
    public async Task SuccessfulSignInsDoNotCount_AndClearEarlierFailures()
    {
        UserInternal user = await AccountClient.CreateAccount(_factory, UserRole.User);
        string address = NewAddress();
        var statuses = new List<int>();

        // More than the ten attempts a minute that the earlier per-login limit allowed; the address allows twenty.
        for (var attempt = 0; attempt < 11; attempt++)
        {
            statuses.Add((await Attempt(user.Login, AccountClient.Password, address)).StatusCode);
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            statuses.Add((await Attempt(user.Login, WrongPassword, address)).StatusCode);
        }

        int afterFailures = (await Attempt(user.Login, AccountClient.Password, address)).StatusCode;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            statuses.Add((await Attempt(user.Login, WrongPassword, address)).StatusCode);
        }

        statuses.Take(11).ShouldAllBe(status => status == StatusCodes.Status200OK);
        statuses.Skip(11).ShouldAllBe(status => status == StatusCodes.Status401Unauthorized);
        afterFailures.ShouldBe(StatusCodes.Status200OK);
    }

    [Test]
    public async Task StrangersLockTheLoginEverywhereExceptTheAddressesItSignedInFrom()
    {
        UserInternal owner = await AccountClient.CreateAccount(_factory, UserRole.Owner);
        string home = NewAddress();
        string attacker = NewAddress();
        string elsewhere = NewAddress();
        (await Attempt(owner.Login, AccountClient.Password, home)).StatusCode.ShouldBe(StatusCodes.Status200OK);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await Attempt(owner.Login, WrongPassword, attacker)).StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        }

        int fromAttacker = (await Attempt(owner.Login, AccountClient.Password, attacker)).StatusCode;
        int fromElsewhere = (await Attempt(owner.Login, AccountClient.Password, elsewhere)).StatusCode;
        int fromHome = (await Attempt(owner.Login, AccountClient.Password, home)).StatusCode;

        fromAttacker.ShouldBe(StatusCodes.Status429TooManyRequests);
        fromElsewhere.ShouldBe(StatusCodes.Status429TooManyRequests);
        fromHome.ShouldBe(StatusCodes.Status200OK);
    }

    [Test]
    public async Task UnknownLoginIsLockedOutLikeAnExistingOne()
    {
        UserInternal user = await AccountClient.CreateAccount(_factory, UserRole.User);
        var missing = $"missing-{Guid.NewGuid():N}";
        string existing = NewAddress();
        string unknown = NewAddress();
        var existingStatuses = new List<int>();
        var unknownStatuses = new List<int>();

        for (var attempt = 0; attempt < 6; attempt++)
        {
            existingStatuses.Add((await Attempt(user.Login, WrongPassword, existing)).StatusCode);
            unknownStatuses.Add((await Attempt(missing, WrongPassword, unknown)).StatusCode);
        }

        unknownStatuses.ShouldBe(existingStatuses);
    }

    private string NewAddress()
    {
        return $"198.51.100.{Interlocked.Increment(ref _addresses)}";
    }

    private async Task<HttpResponse> Attempt(string login, string password, string address)
    {
        HttpContext context = await _factory.Server.SendAsync(request =>
        {
            request.Connection.RemoteIpAddress = IPAddress.Parse(address);
            request.Request.Method = HttpMethods.Post;
            request.Request.Path = "/api/auth/login";
            request.Request.ContentType = "application/json";
            request.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new { login, password }));
        });

        return context.Response;
    }
}