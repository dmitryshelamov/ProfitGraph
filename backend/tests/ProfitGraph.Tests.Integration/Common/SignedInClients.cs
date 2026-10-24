using System.Net;
using System.Net.Http.Json;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.CreateUser;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Tests.Integration.Common;

/// <summary>
///     Signs in a fresh user for every client, so data created by one client is invisible to another.
/// </summary>
internal static class SignedInClients
{
    private const string Password = "correct-horse-battery";

    internal static async Task<HttpClient> CreateSignedInClient(this ProfitGraphApiFactory factory, UserRole role = UserRole.User)
    {
        return await factory.SignIn(await factory.CreateAccount(role));
    }

    internal static async Task<HttpClient> SignIn(this ProfitGraphApiFactory factory, UserInternal user)
    {
        HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new { login = user.Login, password = Password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return client;
    }

    /// <summary>
    ///     Signs the user in once more and returns the session cookie as a <c>Cookie</c> header value, for clients that
    ///     keep no cookies themselves, such as SignalR.
    /// </summary>
    internal static async Task<string> SessionCookie(this ProfitGraphApiFactory factory, UserInternal user)
    {
        using HttpClient client = factory.Server.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new { login = user.Login, password = Password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0]));
    }

    internal static async Task<UserInternal> CreateAccount(this ProfitGraphApiFactory factory, UserRole role)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CreateUserCommand($"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}", Password, role));
    }
}