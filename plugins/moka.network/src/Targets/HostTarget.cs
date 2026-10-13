using System.Net;

namespace Moka.Network.Targets;

/// <summary>A machine to probe as typed: a host name in ASCII form, or an address, which needs no lookup.</summary>
public sealed record HostTarget(string Host, IPAddress? Address)
{
	public bool IsAddress => Address is not null;
}
