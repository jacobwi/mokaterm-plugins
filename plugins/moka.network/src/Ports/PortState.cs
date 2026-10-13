namespace Moka.Network.Ports;

/// <summary>What a connection to one port found.</summary>
public enum PortState
{
	/// <summary>Something accepted the connection.</summary>
	Open,

	/// <summary>The machine answered that nothing listens there.</summary>
	Refused,

	/// <summary>Nothing answered in time: a firewall dropping the attempt, or no machine at that address.</summary>
	TimedOut,

	/// <summary>The network said there is no way to the machine.</summary>
	Unreachable,

	/// <summary>Anything else, with the reason beside it.</summary>
	Failed,
}
