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
        UserInternal user = await factory.CreateAccount(role);
        HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new { login = user.Login, password = Password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return client;
    }

    internal static async Task<UserInternal> CreateAccount(this ProfitGraphApiFactory factory, UserRole role)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CreateUserCommand($"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}", Password, role));
    }
}