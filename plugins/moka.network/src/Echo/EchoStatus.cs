namespace Moka.Network.Echo;

/// <summary>What became of one echo request, in the few kinds the views tell apart.</summary>
public enum EchoStatus
{
	/// <summary>The destination answered.</summary>
	Reply,

	/// <summary>Nothing came back in time.</summary>
	TimedOut,

	/// <summary>A router on the way dropped it when its time to live ran out, which is what traceroute asks for.</summary>
	TtlExpired,

	/// <summary>A router or the destination said it cannot be reached.</summary>
	Unreachable,

	/// <summary>Anything else, with the reason in <see cref="EchoReply.Detail"/>.</summary>
	Failed,
}
