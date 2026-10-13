namespace Moka.Network.Lookup;

/// <summary>Who the lookup asks. Stored by name in the settings, so new kinds go last.</summary>
public enum DnsResolverKind
{
	/// <summary>The DNS servers this machine's network settings name.</summary>
	System,
	Cloudflare,
	Google,
	Quad9,
	Custom,
}
