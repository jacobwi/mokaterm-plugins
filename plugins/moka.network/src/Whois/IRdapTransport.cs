namespace Moka.Network.Whois;

/// <summary>Puts one RDAP request to one server over HTTPS. The client follows referrals over this, so tests can answer from memory.</summary>
public interface IRdapTransport
{
	/// <summary>
	/// Gets <paramref name="url"/> and reads its whole body, at most 1 MiB, within <paramref name="timeout"/>. Throws
	/// <see cref="Targets.ProbeException"/> when the server could not be reached or did not answer in time; any HTTP
	/// status comes back as it is, for the client to read.
	/// </summary>
	Task<RdapReply> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken);
}
