using System.Net;

namespace Moka.Network.Echo;

/// <summary>
/// One echo request's outcome. <see cref="From"/> is the machine that answered, which for an expired TTL is the router
/// that dropped the request; it is null when the answer named nobody (a time out, or the ping program fallback on
/// Linux, which can report an expired TTL without the router's address).
/// </summary>
public sealed record EchoReply(EchoStatus Status, IPAddress? From, double? Milliseconds, string? Detail = null)
{
	public static EchoReply TimedOut { get; } = new(EchoStatus.TimedOut, null, null);
}
