namespace Moka.Network.Whois;

/// <summary>One entry of the RDAP bootstrap list: the top level domains it serves and the RDAP urls that serve them.</summary>
public sealed record RdapService(IReadOnlyList<string> Tlds, IReadOnlyList<string> Urls)
{
	/// <summary>The first url that speaks HTTPS. A service with only plain HTTP urls is not used.</summary>
	public string? HttpsUrl => Urls.FirstOrDefault(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

	/// <summary>True when this service answers for <paramref name="tld"/>, which DNS compares without case.</summary>
	public bool Serves(string tld) => Tlds.Any(served => string.Equals(served, tld, StringComparison.OrdinalIgnoreCase));
}
