using System.Net;
using System.Net.Sockets;

using Microsoft.Extensions.Options;

namespace ProfitGraph.Api.Common.Authentication;

/// <summary>
///     Locks a login out of sign-in after consecutive failed attempts, with a lockout that doubles up to a cap and ends on
///     a successful sign-in (ADR 0043). Failures are counted per login and per login and client address. A client
///     address that has signed in to the login before is held only by its own failures, so a stranger who fails on the
///     login cannot lock its owner out of the addresses the owner uses. The state lives in this process.
/// </summary>
public sealed class SignInThrottle(IOptions<AuthOptions> options, TimeProvider clock)
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<(string Login, string? Address), SignInFailures> _failures = new Dictionary<(string Login, string? Address), SignInFailures>();
    private readonly Dictionary<string, List<string>> _knownAddresses = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    private DateTimeOffset _sweptAt = DateTimeOffset.MinValue;

    /// <summary>
    ///     Starts an attempt, counted as a failure until <see cref="Succeeded" /> clears it, so parallel attempts cannot
    ///     pass a lockout that the first of them earns. Returns false with the time left while the login is locked out.
    /// </summary>
    public bool TryBegin(string login, IPAddress? address, out TimeSpan retryAfter)
    {
        string key = Normalize(login);
        string source = Source(address);
        DateTimeOffset now = clock.GetUtcNow();

        lock (_gate)
        {
            Sweep(now);
            bool known = _knownAddresses.TryGetValue(key, out List<string>? addresses) && addresses.Contains(source);
            TimeSpan fromAddress = Remaining((key, source), now);
            TimeSpan fromLogin = known ? TimeSpan.Zero : Remaining((key, null), now);
            retryAfter = fromAddress > fromLogin ? fromAddress : fromLogin;

            if (retryAfter > TimeSpan.Zero)
            {
                return false;
            }

            Fail((key, source), now);

            if (!known)
            {
                Fail((key, null), now);
            }

            return true;
        }
    }

    /// <summary>
    ///     Clears the failures of the login and remembers the client address as one the login signs in from.
    /// </summary>
    public void Succeeded(string login, IPAddress? address)
    {
        string key = Normalize(login);
        string source = Source(address);

        lock (_gate)
        {
            _failures.Remove((key, null));
            _failures.Remove((key, source));

            if (!_knownAddresses.TryGetValue(key, out List<string>? addresses))
            {
                addresses = [];
                _knownAddresses[key] = addresses;
            }

            addresses.Remove(source);
            addresses.Insert(index: 0, source);

            if (addresses.Count > options.Value.KnownAddressesPerLogin)
            {
                addresses.RemoveAt(addresses.Count - 1);
            }
        }
    }

    internal static TimeSpan Lockout(int failures, AuthOptions options)
    {
        int doublings = Math.Min(failures - options.FreeFailures, val2: 16);
        TimeSpan lockout = options.FirstLockout * Math.Pow(x: 2, doublings);

        return lockout < options.MaximumLockout ? lockout : options.MaximumLockout;
    }

    private static string Normalize(string login)
    {
        return login.Trim().ToUpperInvariant();
    }

    /// <summary>
    ///     IPv6 clients usually own a whole /64 and rotate addresses inside it, so the prefix stands for the client.
    /// </summary>
    private static string Source(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        byte[] bytes = address.GetAddressBytes();
        Array.Clear(bytes, index: 8, length: 8);

        return $"{new IPAddress(bytes)}/64";
    }

    private TimeSpan Remaining((string Login, string? Address) key, DateTimeOffset now)
    {
        return _failures.TryGetValue(key, out SignInFailures? failures) ? failures.Remaining(now) : TimeSpan.Zero;
    }

    private void Fail((string Login, string? Address) key, DateTimeOffset now)
    {
        if (!_failures.TryGetValue(key, out SignInFailures? failures))
        {
            failures = new SignInFailures();
            _failures[key] = failures;
        }

        failures.Fail(now, options.Value);
    }

    /// <summary>
    ///     Forgets failures that ended long ago, so logins tried once do not accumulate.
    /// </summary>
    private void Sweep(DateTimeOffset now)
    {
        if (now - _sweptAt < options.Value.FailureSweepInterval)
        {
            return;
        }

        _sweptAt = now;

        foreach (((string Login, string? Address) key, SignInFailures failures) in _failures.ToArray())
        {
            if (failures.Remaining(now) == TimeSpan.Zero && now - failures.LastFailureAt >= options.Value.ForgetFailuresAfter)
            {
                _failures.Remove(key);
            }
        }
    }
}