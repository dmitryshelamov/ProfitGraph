namespace ProfitGraph.Api.Accounts.Contracts;

public sealed record SignInRequest(string? Login, string? Password);