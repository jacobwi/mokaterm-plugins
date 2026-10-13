namespace Moka.Certificates.Tls;

public enum HostNameResult
{
	NoMatch,
	Matches,

	/// <summary>Only the common name matches, which browsers ignore since 2017.</summary>
	CommonNameOnly,
}
