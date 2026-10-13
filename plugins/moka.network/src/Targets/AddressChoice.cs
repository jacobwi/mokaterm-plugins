namespace Moka.Network.Targets;

/// <summary>Which address of a dual-stack name a probe goes to.</summary>
public enum AddressChoice
{
	/// <summary>Whichever the system lists first, which is what other programs on this machine would use.</summary>
	Any,
	IPv4,
	IPv6,
}
