using System.Threading.RateLimiting;

using MediatR;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using ProfitGraph.Api.Accounts.Contracts;
using ProfitGraph.Api.Accounts.Converters;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.GetUser;
using ProfitGraph.Application.Accounts.SignIn;

namespace ProfitGraph.Api.Accounts;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender, SignInLoginLimiter loginLimiter) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.SignInPolicy)]
    public async Task<ActionResult<UserResponse>> Login(SignInRequest request, CancellationToken cancellationToken)
    {
        string login = request.Login ?? string.Empty;
        using RateLimitLease lease = loginLimiter.Acquire(login);

        if (!lease.IsAcquired)
        {
            return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many sign-in attempts");
        }

        UserInternal? user = await sender.Send(new SignInCommand(login, request.Password ?? string.Empty), cancellationToken);

        if (user is null)
        {
            ProblemDetails problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, StatusCodes.Status401Unauthorized, "Invalid credentials");
            problem.Extensions["code"] = "invalid_credentials";

            return Unauthorized(problem);
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthenticationSetup.Principal(user));

        return user.ToResponse();
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        UserInternal? user = AuthenticationSetup.UserId(User) is { } id ? await sender.Send(new GetUserQuery(id), cancellationToken) : null;

        return user is null ? Unauthorized() : user.ToResponse();
    }
}