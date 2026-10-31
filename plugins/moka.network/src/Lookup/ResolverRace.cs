using System.Diagnostics;
using System.Net;

namespace Moka.Network.Lookup;

/// <summary>One resolver taking part in a race: its name for the table, and the servers that answer behind it.</summary>
public sealed record ResolverRaceEntry(string Label, IReadOnlyList<IPEndPoint> Servers);

/// <summary>
/// How one resolver fared in the race: its answer if it gave one, its failure if it did not, and how long the whole try
/// took, fallback servers included.
/// </summary>
public sealed record ResolverRaceResult(ResolverRaceEntry Entry, DnsLookupResult? Result, string? Failure, TimeSpan Elapsed)
{
	/// <summary>True when the resolver produced an exchange, whatever its response code: NXDOMAIN and SERVFAIL are answers too.</summary>
	public bool Answered => Result is not null;
}

/// <summary>
/// Asks several resolvers the same question at the same moment, so one slow server cannot hide the others' pace, and
/// ranks them fastest first. A resolver that never answered is called out after the ones that did, with the reason.
/// </summary>
public static class ResolverRace
{
	public static async Task<IReadOnlyList<ResolverRaceResult>> RunAsync(
		IReadOnlyList<ResolverRaceEntry> entries,
		string name,
		int type,
		TimeSpan timeout,
		Func<IReadOnlyList<IPEndPoint>, string, int, TimeSpan, CancellationToken, Task<DnsLookupResult>> lookup,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entries);
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(lookup);
		ResolverRaceResult[] results = await Task.WhenAll(entries.Select(entry => AskAsync(entry, name, type, timeout, lookup, cancellationToken)));
		return [.. results
			.Select((result, index) => (result, index))
			.OrderBy(pair => pair.result.Answered ? 0 : 1)
			.ThenBy(pair => pair.result.Elapsed)
			.ThenBy(pair => pair.index)
			.Select(pair => pair.result)];
	}

	/// <summary>The first A or AAAA record's data, or the response code's name when the answer holds no address: the one-line story of a race row.</summary>
	public static string AnswerSummary(DnsLookupResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		DnsRecord? address = result.Exchange.Message.Answers.FirstOrDefault(record =>
			!record.Malformed && (record.Type == (int)DnsRecordType.A || record.Type == (int)DnsRecordType.AAAA));
		return address?.Data ?? result.Exchange.Message.ResponseCodeName;
	}

	private static async Task<ResolverRaceResult> AskAsync(
		ResolverRaceEntry entry,
		string name,
		int type,
		TimeSpan timeout,
		Func<IReadOnlyList<IPEndPoint>, string, int, TimeSpan, CancellationToken, Task<DnsLookupResult>> lookup,
		CancellationToken cancellationToken)
	{
		long started = Stopwatch.GetTimestamp();
		try
		{
			DnsLookupResult result = await lookup(entry.Servers, name, type, timeout, cancellationToken);
			return new ResolverRaceResult(entry, result, null, Stopwatch.GetElapsedTime(started));
		}
		catch (DnsLookupException ex)
		{
			return new ResolverRaceResult(entry, null, ex.Message, Stopwatch.GetElapsedTime(started));
		}
	}
}
