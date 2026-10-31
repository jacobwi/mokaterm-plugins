using System.Net;

namespace Moka.Network.Lookup.Mapping;

/// <summary>One resolver's answer to the one question, for the table that lines them up side by side.</summary>
public sealed record ResolverMatrixRow
{
	/// <summary>The resolver as the table names it: <c>Cloudflare 1.1.1.1</c>.</summary>
	public required string Resolver { get; init; }

	/// <summary>True when a reply arrived, whatever its response code.</summary>
	public required bool Answered { get; init; }

	/// <summary>The answers in one line, or null when there were none or nobody answered.</summary>
	public required string? Answer { get; init; }

	/// <summary>The response code's name, or null when nobody answered.</summary>
	public required string? Code { get; init; }

	/// <summary>How long the answering server took; a secondary column, not the point of the table.</summary>
	public required TimeSpan Elapsed { get; init; }

	/// <summary>Why this resolver could not be asked at all, or how asking it failed, worded.</summary>
	public required string? Failure { get; init; }

	/// <summary>The answers and the response code as one comparable string, order of records aside.</summary>
	public required string Fingerprint { get; init; }
}

/// <summary>Every resolver's answer to the one question, with the disagreements called out.</summary>
public sealed record ResolverMatrixResult
{
	public required IReadOnlyList<ResolverMatrixRow> Rows { get; init; }

	/// <summary>One worded note per distinct answer the resolvers did not agree on; empty when they all match.</summary>
	public required IReadOnlyList<string> Disagreements { get; init; }
}

/// <summary>
/// Asks the same question of every resolver in turn and lines the answers up: which answer each gave, what code, and
/// where they disagree. Correctness, not speed — the race belongs to the DNS tab. Pure logic driven by a query
/// function, the way <see cref="DnsLookup.RunAsync"/> is meant to be reused.
/// </summary>
public static class ResolverMatrix
{
	/// <summary>How many records of one answer are spelled out in a row; the rest are counted.</summary>
	public const int MaxListedRecords = 3;

	/// <summary>One question put to a list of servers, the way <see cref="DnsLookup.RunAsync"/> asks it.</summary>
	public delegate Task<DnsLookupResult> Query(IReadOnlyList<IPEndPoint> servers, string name, int type, CancellationToken cancellationToken);

	/// <summary>
	/// Asks <paramref name="kinds"/> (with <paramref name="custom"/> for the custom kind) in turn; the timeout travels
	/// inside <paramref name="query"/>. A resolver that answers with an error code has answered: its row shows the code,
	/// and it counts towards the disagreements.
	/// </summary>
	public static async Task<ResolverMatrixResult> RunAsync(IEnumerable<DnsResolverKind> kinds, string? custom, Query query, string name, int type, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		List<ResolverMatrixRow> rows = [];
		foreach (DnsResolverKind kind in kinds)
		{
			IReadOnlyList<IPEndPoint> servers = DnsResolvers.Servers(kind, custom, out string? problem);
			if (servers.Count == 0)
			{
				rows.Add(new ResolverMatrixRow
				{
					Resolver = DnsResolvers.Label(kind),
					Answered = false,
					Answer = null,
					Code = null,
					Elapsed = TimeSpan.Zero,
					Failure = problem ?? "There is no DNS server to ask.",
					Fingerprint = "",
				});
				continue;
			}

			try
			{
				DnsLookupResult result = await query(servers, name, type, cancellationToken);
				DnsMessage message = result.Exchange.Message;
				rows.Add(new ResolverMatrixRow
				{
					Resolver = DnsResolvers.Label(kind),
					Answered = true,
					Answer = Describe(message.Answers),
					Code = message.ResponseCodeName,
					Elapsed = result.Exchange.Elapsed,
					Failure = result.EarlierFailures.Count > 0 ? string.Join(" ", result.EarlierFailures) : null,
					Fingerprint = Fingerprint(message),
				});
			}
			catch (DnsLookupException ex)
			{
				rows.Add(new ResolverMatrixRow
				{
					Resolver = DnsResolvers.Label(kind),
					Answered = false,
					Answer = null,
					Code = null,
					Elapsed = TimeSpan.Zero,
					Failure = ex.Message,
					Fingerprint = "",
				});
			}
		}

		return new ResolverMatrixResult { Rows = [.. rows], Disagreements = Disagreements(rows) };
	}

	/// <summary>The records of one answer as one line, the first few spelled out and the rest counted.</summary>
	private static string? Describe(IReadOnlyList<DnsRecord> records)
	{
		if (records.Count == 0)
		{
			return null;
		}

		string first = $"{records[0].TypeName} {records[0].Data}";
		return records.Count == 1 ? first : $"{first} (+{records.Count - 1} more)";
	}

	/// <summary>Answers and response code as one string, records sorted, so a reordered answer still compares equal.</summary>
	private static string Fingerprint(DnsMessage message) => string.Join(
		'|',
		[message.ResponseCodeName, .. message.Answers.Select(record => $"{record.Type} {record.Data}").Order(StringComparer.Ordinal)]);

	private static IReadOnlyList<string> Disagreements(List<ResolverMatrixRow> rows)
	{
		List<IGrouping<string, ResolverMatrixRow>> answers = [.. rows.Where(row => row.Answered).GroupBy(row => row.Fingerprint)];
		if (answers.Count <= 1)
		{
			return [];
		}

		return
		[
			.. answers.Select(group => $"{string.Join(", ", group.Select(row => row.Resolver))} {(group.Count() == 1 ? "says" : "say")} {DescribeRow(group.First())}."),
			"The resolvers above do not agree on this name. DNS answers where they were asked, and caches expire at their own pace, so this is often one answer simply being older than another.",
		];
	}

	private static string DescribeRow(ResolverMatrixRow row) => row.Answer is { } answer
		? $"{row.Code} with {answer}"
		: $"{row.Code} with no records";
}
