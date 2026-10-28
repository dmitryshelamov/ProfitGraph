using System.Net;

using MediatR;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.BlockUser;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Accounts;

/// <summary>
///     Blocking an account ends its realtime connections as well as its HTTP session (ADR 0043). The clients use
///     WebSockets, which, unlike long polling, send no HTTP request after the connection opens.
/// </summary>
public sealed class BlockedAccountConnectionsTests
{
    private static readonly string[] Hubs = ["/ws/backtests", "/ws/optimizations", "/ws/live-prices", "/ws/forming-candles"];
    private ProfitGraphApiFactory _factory = null!;
    private UserInternal _owner = null!;
    private HttpClient _ownerClient = null!;

    [OneTimeSetUp]
    public async Task StartApi()
    {
        _factory = new ProfitGraphApiFactory();
        _owner = await _factory.CreateAccount(UserRole.Owner);
        _ownerClient = await _factory.SignIn(_owner);
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        _ownerClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task BlockingClosesTheAccountsOpenConnectionsOnEveryHub_AndRefusesNewOnes()
    {
        UserInternal user = await _factory.CreateAccount(UserRole.User);
        UserInternal other = await _factory.CreateAccount(UserRole.User);
        string cookie = await _factory.SessionCookie(user);
        List<HubConnection> connections = [];
        List<Task> closed = [];

        foreach (string hub in Hubs)
        {
            HubConnection connection = await Connect(hub, cookie);
            connections.Add(connection);
            closed.Add(Closed(connection));
        }

        await using HubConnection bystander = await Connect("/ws/live-prices", await _factory.SessionCookie(other));

        (await _ownerClient.PostAsync($"/api/users/{user.Id}/block", content: null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await Task.WhenAll(closed).WaitAsync(TimeSpan.FromSeconds(10));
        connections.ShouldAllBe(connection => connection.State == HubConnectionState.Disconnected);
        bystander.State.ShouldBe(HubConnectionState.Connected);
        await Should.ThrowAsync<HttpRequestException>(() => Connect("/ws/live-prices", cookie));

        foreach (HubConnection connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    [Test]
    public async Task ConnectionThatABlockMissedEndsOnItsNextCall()
    {
        UserInternal user = await _factory.CreateAccount(UserRole.User);
        await using HubConnection connection = await Connect("/ws/live-prices", await _factory.SessionCookie(user));
        Task closed = Closed(connection);

        // Blocks the account without this process's controller, as another API instance would.
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new BlockUserCommand(_owner.Id, user.Id));
        }

        // The call fails with the refusal or with the closed connection, whichever reaches the client first.
        await Should.ThrowAsync<Exception>(() => connection.InvokeAsync("Subscribe", Guid.NewGuid()));

        await closed.WaitAsync(TimeSpan.FromSeconds(10));
        connection.State.ShouldBe(HubConnectionState.Disconnected);
    }

    private static Task Closed(HubConnection connection)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ =>
        {
            closed.TrySetResult();

            return Task.CompletedTask;
        };

        return closed.Task;
    }

    private async Task<HubConnection> Connect(string hub, string cookie)
    {
        TestServer server = _factory.Server;
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, hub),
                options =>
                {
                    options.Transports = HttpTransportType.WebSockets;
                    options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                    options.Headers["Cookie"] = cookie;
                    options.WebSocketFactory = async (context, cancellationToken) =>
                    {
                        WebSocketClient client = server.CreateWebSocketClient();
                        client.ConfigureRequest = request => request.Headers.Cookie = cookie;

                        return await client.ConnectAsync(context.Uri, cancellationToken);
                    };
                })
            .Build();
        await connection.StartAsync();

        return connection;
    }
}