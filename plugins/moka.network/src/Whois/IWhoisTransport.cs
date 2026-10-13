namespace Moka.Network.Whois;

/// <summary>Puts one query to one whois server. The client follows referrals over this, so tests can answer from memory.</summary>
public interface IWhoisTransport
{
	/// <summary>
	/// Sends <paramref name="query"/> to <paramref name="server"/> on port 43 and reads until the server closes, at most
	/// <paramref name="maxBytes"/>, within <paramref name="timeout"/>. Throws <see cref="Targets.ProbeException"/> when
	/// nothing could be read.
	/// </summary>
	Task<WhoisRaw> AskAsync(string server, string query, int maxBytes, TimeSpan timeout, CancellationToken cancellationToken);
}
