using System.Net;

namespace Moka.Network.Lookup.Mapping;

/// <summary>One name asked on the way along the chain: the CNAME that points onward, or the records that end it.</summary>
public sealed record CnameHop
{
	/// <summary>The name this hop asked about.</summary>
	public required string Name { get; init; }

	/// <summary>The records the answer carried: one CNAME pointing on, or the final records of the asked type.</summary>
	public required IReadOnlyList<DnsRecord> Records { get; init; }

	public required IPEndPoint Server { get; init; }

	public required TimeSpan Elapsed { get; init; }
}

/// <summary>How the chain ended.</summary>
public enum CnameChainOutcome
{
	/// <summary>A name answered with records of the asked type, or honestly said it has none.</summary>
	Complete,

	/// <summary>A CNAME points at the very name that owns it.</summary>
	SelfReference,

	/// <summary>The chain came back to a name it had already visited.</summary>
	Loop,

	/// <summary>The cap of ten hops was reached while CNAMEs kept coming.</summary>
	TooLong,

	/// <summary>A lookup failed, or a target could not be asked for.</summary>
	Failed,
}

/// <summary>The chain of CNAMEs from a name to whatever ends it, hop by hop.</summary>
public sealed record CnameChainResult
{
	public required IReadOnlyList<CnameHop> Hops { get; init; }

	/// <summary>The name the chain ends on: the last name asked about.</summary>
	public required string FinalName { get; init; }

	public required CnameChainOutcome Outcome { get; init; }

	/// <summary>The last word on the chain, worded for the map.</summary>
	public required string Summary { get; init; }
}

/// <summary>
/// Follows the CNAMEs from a name until records of the asked type arrive, drawing each hop. A resolver does this in
/// one go; this asks again per hop so the chain itself is what shows. Pure logic driven by a lookup function, the way
/// <see cref="DnsLookup.RunAsync"/> is meant to be reused.
/// </summary>
public static class CnameChain
{
	/// <summary>The most hops followed; the largest chains real zones carry are two or three.</summary>
	public const int MaxHops = 10;

	/// <summary>One name asked through the resolver, the way <see cref="DnsLookup.RunAsync"/> asks it.</summary>
	public delegate Task<DnsLookupResult> Lookup(string name, int type, CancellationToken cancellationToken);

	public static async Task<CnameChainResult> RunAsync(Lookup lookup, string name, int type, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(lookup);
		List<CnameHop> hops = [];
		HashSet<string> seen = [name];
		string current = name;

		for (int count = 1; ; count++)
		{
			DnsLookupResult result;
			try
			{
				result = await lookup(current, type, cancellationToken);
			}
			catch (DnsLookupException ex)
			{
				return Finished(hops, current, CnameChainOutcome.Failed, $"The chain stopped at {current}: {ex.Message}");
			}

			DnsMessage message = result.Exchange.Message;
			List<DnsRecord> cnames = [.. message.Answers.Where(record => record.Type == (int)DnsRecordType.CNAME && !record.Malformed && DnsName.SameName(record.Name, current))];
			hops.Add(new CnameHop
			{
				Name = current,
				Records = cnames.Count > 0 ? cnames : message.Answers,
				Server = result.Exchange.Server,
				Elapsed = result.Exchange.Elapsed,
			});

			if (message.ResponseCode != 0)
			{
				string meaning = DnsNames.ResponseCodeMeaning(message.ResponseCode) ?? $"{message.ResponseCodeName} is not a code this tool has words for.";
				return Finished(hops, current, CnameChainOutcome.Failed, $"{result.Exchange.Server} said {message.ResponseCodeName} for {current}: {meaning}");
			}

			if (cnames.Count == 0)
			{
				return message.Answers.Count > 0
					? Finished(hops, current, CnameChainOutcome.Complete, $"{current} has {Plural(message.Answers.Count, $"{DnsNames.TypeName(type)} record")}; the chain ends here.")
					: Finished(hops, current, CnameChainOutcome.Complete, $"{current} has no {DnsNames.TypeName(type)} records; the chain ends here.");
			}

			string target = cnames[0].Data.TrimEnd('.');
			if (DnsName.SameName(target, current))
			{
				return Finished(hops, current, CnameChainOutcome.SelfReference, $"{current} points at itself, which no zone should ask any resolver to follow.");
			}

			if (!seen.Add(target))
			{
				return Finished(hops, target, CnameChainOutcome.Loop, $"The chain loops: it came back to {target} after already visiting it.");
			}

			if (count >= MaxHops)
			{
				return Finished(hops, target, CnameChainOutcome.TooLong, $"The chain was cut at {MaxHops} hops, still on CNAMEs. That is longer than this map follows.");
			}

			current = target;
		}
	}

	private static CnameChainResult Finished(List<CnameHop> hops, string finalName, CnameChainOutcome outcome, string summary) => new()
	{
		Hops = [.. hops],
		FinalName = finalName,
		Outcome = outcome,
		Summary = summary,
	};

	private static string Plural(int count, string what) => count == 1 ? $"one {what}" : $"{count} {what}s";
}
