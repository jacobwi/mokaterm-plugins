namespace Moka.Network.Echo;

/// <summary>Why a trace stopped.</summary>
public enum TraceEnd
{
	/// <summary>The destination answered.</summary>
	Reached,

	/// <summary>A router or the destination said the destination cannot be reached; further hops would say the same.</summary>
	Unreachable,

	/// <summary>The last TTL went out without reaching the destination.</summary>
	OutOfHops,
}
