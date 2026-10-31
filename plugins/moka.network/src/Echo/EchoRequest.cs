namespace Moka.Network.Echo;

/// <summary>How one echo request is sent: its time to live, how long to wait, how many bytes it carries, and whether it may be broken up on the way.</summary>
public sealed record EchoRequest(int Ttl, TimeSpan Timeout, int PayloadSize, bool DontFragment = false)
{
	public const int MinPayload = 0;

	/// <summary>The most Windows ping sends; past it the request would not fit in one IP packet's 16 bit length.</summary>
	public const int MaxPayload = 65500;

	public const int DefaultPayload = 32;

	public const int MinTtl = 1;

	public const int MaxTtl = 255;

	public const int DefaultTtl = 128;
}
