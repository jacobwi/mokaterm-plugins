using System.Net;

namespace Moka.Network.Lookup;

/// <summary>
/// A question put to a list of servers in turn until one answers. A server that answers with an error code has
/// answered: SERVFAIL or NXDOMAIN is a result to show, not a reason to ask the next one.
/// </summary>
public static class DnsLookup
{
	public static async Task<DnsLookupResult> RunAsync(IReadOnlyList<IPEndPoint> servers, string name, int type, TimeSpan timeout, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(servers);
		if (servers.Count == 0)
		{
			throw new DnsLookupException("There is no DNS server to ask.");
		}

		List<string> failures = [];
		foreach (IPEndPoint server in servers.Take(DnsResolvers.MaxServersTried))
		{
			try
			{
				DnsExchange exchange = await DnsClient.ExchangeAsync(server, name, type, timeout, cancellationToken);
				return new DnsLookupResult(exchange, failures);
			}
			catch (DnsLookupException ex)
			{
				failures.Add(ex.Message);
			}
		}

		throw new DnsLookupException(string.Join(" ", failures));
	}

	/// <summary>The first name a PTR answer gives for <paramref name="address"/>, or null when there is none or nobody answered.</summary>
	public static async Task<string?> ReverseAsync(IReadOnlyList<IPEndPoint> servers, IPAddress address, TimeSpan timeout, CancellationToken cancellationToken)
	{
		try
		{
			DnsLookupResult result = await RunAsync(servers, ReverseName.For(address), (int)DnsRecordType.PTR, timeout, cancellationToken);
			return result.Exchange.Message.Answers.FirstOrDefault(record => record.Type == (int)DnsRecordType.PTR && !record.Malformed)?.Data;
		}
		catch (DnsLookupException)
		{
			return null;
		}
	}
}
