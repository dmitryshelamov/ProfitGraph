using System.Net;
using System.Net.Http.Json;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.CreateUser;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Accounts;

/// <summary>
///     Creates accounts directly through the application and signs in through HTTP with a cookie-keeping client.
/// </summary>
internal static class AccountClient
{
    internal const string Password = "correct-horse-battery";

    internal static async Task<UserInternal> CreateAccount(ProfitGraphApiFactory factory, UserRole role, string? login = null)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CreateUserCommand(login ?? $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}", Password, role));
    }

    internal static async Task<HttpClient> SignedIn(ProfitGraphApiFactory factory, string login, string password = Password)
    {
        HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new { login, password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return client;
    }
}