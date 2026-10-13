using System.Net;

namespace Moka.Network.Echo;

/// <summary>A ping run: where, how many (0 for until stopped), how often, and each request's shape.</summary>
public sealed record PingPlan(IPAddress Address, int Count, TimeSpan Interval, EchoRequest Request)
{
	public const int MaxCount = 10000;

	/// <summary>The shortest gap between requests, the same floor Linux ping keeps for an unprivileged user.</summary>
	public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(200);

	public static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(60);

	public bool Continuous => Count == 0;
}
