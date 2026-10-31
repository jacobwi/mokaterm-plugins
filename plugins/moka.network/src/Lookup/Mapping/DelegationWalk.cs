using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Moka.Network.Lookup.Mapping;

/// <summary>One question put to one server on the way down the delegation chain, and what came of it.</summary>
public sealed record DelegationStep
{
	/// <summary>Who was asked, as well as an address can say: <c>a.root-servers.net</c> for a root, the NS name after a referral.</summary>
	public required string ServerName { get; init; }

	public required IPEndPoint Server { get; init; }

	/// <summary>The reply and how long it took, or null when this server never answered at all.</summary>
	public DnsExchange? Exchange { get; init; }

	/// <summary>What this step means for the walk, worded for the map: <c>referred example.com to the servers of com</c>.</summary>
	public required string Outcome { get; init; }

	public required DelegationStepKind Kind { get; init; }
}

/// <summary>How a step ended, which is what the map colors it by.</summary>
public enum DelegationStepKind
{
	/// <summary>The server pointed at the servers of a smaller zone; the walk goes on.</summary>
	Referral,

	/// <summary>The server answered the question itself.</summary>
	Answer,

	/// <summary>The server said the name exists but has no records of the asked type.</summary>
	NoData,

	/// <summary>The server said no, or never said anything.</summary>
	Failed,
}

/// <summary>
/// How a name resolves when nobody recurses for it: the roots are asked directly, each referral is followed by asking
/// the named server, and the chain is drawn level by level. Pragmatic, not RFC-complete: one NS of each referral is
/// followed (the first), its glue is taken from the additional section, and when the glue is missing the server that
/// made the referral is asked for the NS's address — honest about the gap when that fails too.
/// </summary>
public sealed record DelegationWalkResult
{
	public required IReadOnlyList<DelegationStep> Steps { get; init; }

	/// <summary>True when a server answered the question, which is where a resolver would stop recursing.</summary>
	public required bool ReachedAnswer { get; init; }

	/// <summary>The last word on the walk, worded for the map.</summary>
	public required string Summary { get; init; }
}

/// <summary>
/// Walks a name's delegation from the roots down: root, the TLD's servers, the domain's servers, and so on until a
/// server answers. Pure logic driven by a query function, so the tests can drive every turn a real walk can take.
/// </summary>
public static class DelegationWalk
{
	/// <summary>How many servers the walk will ask before it gives up; the longest real chain is four or five.</summary>
	public const int MaxSteps = 8;

	/// <summary>How many NS names of one referral are drawn as the list; the rest are counted, not listed.</summary>
	public const int MaxListedServers = 4;

	/// <summary>The roots asked first, in order, until one answers. Four of the thirteen, one per operator, all IPv4.</summary>
	public static readonly IReadOnlyList<(string Name, IPEndPoint Server)> Roots =
	[
		("a.root-servers.net", new IPEndPoint(IPAddress.Parse("198.41.0.4"), DnsServerAddress.DefaultPort)),
		("b.root-servers.net", new IPEndPoint(IPAddress.Parse("199.9.14.201"), DnsServerAddress.DefaultPort)),
		("c.root-servers.net", new IPEndPoint(IPAddress.Parse("192.33.4.12"), DnsServerAddress.DefaultPort)),
		("d.root-servers.net", new IPEndPoint(IPAddress.Parse("199.7.91.13"), DnsServerAddress.DefaultPort)),
	];

	/// <summary>One question put to one server, the way <see cref="DnsClient.ExchangeAsync"/> puts it.</summary>
	public delegate Task<DnsExchange> Query(IPEndPoint server, string name, int type, CancellationToken cancellationToken);

	public static async Task<DelegationWalkResult> RunAsync(Query query, string name, int type, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		List<DelegationStep> steps = [];
		HashSet<string> seenZones = [DnsName.Root];

		// The roots first: each is tried until one answers, since one lost datagram should not end the walk.
		(IPEndPoint Server, DnsExchange Exchange)? answered = null;
		foreach ((string rootName, IPEndPoint server) in Roots)
		{
			(DnsExchange? exchange, string? failure) = await AskAsync(query, server, name, type, cancellationToken);
			steps.Add(new DelegationStep
			{
				ServerName = rootName,
				Server = server,
				Exchange = exchange,
				Outcome = exchange is { } ? StepOutcome(exchange) : failure!,
				Kind = exchange is { } ? StepKind(exchange) : DelegationStepKind.Failed,
			});
			if (exchange is { })
			{
				answered = (server, exchange);
				break;
			}
		}

		if (answered is null)
		{
			return new DelegationWalkResult
			{
				Steps = [.. steps],
				ReachedAnswer = false,
				Summary = "No root server answered, so the walk cannot start. This machine may be offline, or a firewall may be blocking DNS to the outside.",
			};
		}

		while (true)
		{
			DnsMessage message = answered.Value.Exchange.Message;
			if (message.ResponseCode != 0)
			{
				string meaning = DnsNames.ResponseCodeMeaning(message.ResponseCode) ?? $"{message.ResponseCodeName} is not a code this tool has words for.";
				return Finished(steps, false, $"The walk stopped at {steps[^1].ServerName}: {meaning}");
			}

			if (message.Answers.Count > 0)
			{
				return Finished(steps, true, $"{steps[^1].ServerName} answered with {Plural(message.Answers.Count, "record")} of the {DnsNames.TypeName(type)} kind asked for.");
			}

			DnsRecord[] referral = [.. message.Authority.Where(record => record.Type == (int)DnsRecordType.NS && !record.Malformed)];
			if (referral.Length == 0)
			{
				return Finished(steps, false, message.Header.Authoritative
					? $"{steps[^1].ServerName} is authoritative and says the name exists but has no {DnsNames.TypeName(type)} records."
					: $"{steps[^1].ServerName} answered with no records and pointed nowhere; the walk stops here.");
			}

			string zone = referral[0].Name;
			if (!seenZones.Add(zone))
			{
				return Finished(steps, false, $"The walk loops: the servers of {zone} refer back to the servers of {zone} rather than answering.");
			}

			if (steps.Count >= MaxSteps)
			{
				return Finished(steps, false, $"The walk gave up after {MaxSteps} servers, still following referrals. That chain is longer than any real delegation.");
			}

			string listed = ListServers(referral);
			steps[^1] = steps[^1] with { Outcome = $"referred {name} to the servers of {zone}: {listed}", Kind = DelegationStepKind.Referral };

			string nsName = referral[0].Data;
			if (!TryGlue(message.Additional, nsName, out IPEndPoint? next))
			{
				// No glue: the server that made the referral is asked for the NS's address, honestly noted when that fails.
				next = await ResolveThroughAsync(query, answered.Value.Server, nsName, cancellationToken);
				if (next is null)
				{
					return Finished(steps, false, $"{zone} is served by {nsName}, whose address neither the referral nor {steps[^1].ServerName} gives, so the walk cannot go on.");
				}

				steps.Add(new DelegationStep
				{
					ServerName = steps[^1].ServerName,
					Server = steps[^1].Server,
					Outcome = $"had no glue for {nsName}; asked for its address and got {next.Address}",
					Kind = DelegationStepKind.Referral,
				});
			}

			(DnsExchange? reply, string? replyFailure) = await AskAsync(query, next, name, type, cancellationToken);
			if (reply is null)
			{
				steps.Add(new DelegationStep
				{
					ServerName = nsName,
					Server = next,
					Outcome = replyFailure!,
					Kind = DelegationStepKind.Failed,
				});
				return Finished(steps, false, $"The servers of {zone} were named as {listed}, but the one asked never replied. The walk stops here.");
			}

			steps.Add(new DelegationStep
			{
				ServerName = nsName,
				Server = next,
				Exchange = reply,
				Outcome = StepOutcome(reply),
				Kind = StepKind(reply),
			});
			answered = (next, reply);
		}
	}

	/// <summary>Asks one server, turning a failure into a step's worth of words rather than an exception.</summary>
	private static async Task<(DnsExchange? Exchange, string? Failure)> AskAsync(Query query, IPEndPoint server, string name, int type, CancellationToken cancellationToken)
	{
		try
		{
			return (await query(server, name, type, cancellationToken), null);
		}
		catch (DnsLookupException ex)
		{
			return (null, ex.Message);
		}
	}

	/// <summary>The NS names of a referral as one list, the first few spelled out and the rest counted.</summary>
	private static string ListServers(IReadOnlyList<DnsRecord> referral)
	{
		string[] names = [.. referral.Select(record => record.Data).Distinct(StringComparer.OrdinalIgnoreCase)];
		return names.Length > MaxListedServers
			? $"{string.Join(", ", names.Take(MaxListedServers))} and {names.Length - MaxListedServers} more"
			: string.Join(", ", names);
	}

	private static bool TryGlue(IReadOnlyList<DnsRecord> additional, string nsName, [NotNullWhen(true)] out IPEndPoint? server)
	{
		server = null;
		DnsRecord? glue = additional
			.Where(record => !record.Malformed && DnsName.SameName(record.Name, nsName) && record.Type is (int)DnsRecordType.A or (int)DnsRecordType.AAAA)
			.OrderBy(record => record.Type)
			.FirstOrDefault();
		if (glue is null || !IPAddress.TryParse(glue.Data, out IPAddress? address))
		{
			return false;
		}

		server = new IPEndPoint(address, DnsServerAddress.DefaultPort);
		return true;
	}

	/// <summary>The address of <paramref name="nsName"/> as the server that named it tells it — no glue, so no other resolver is involved.</summary>
	private static async Task<IPEndPoint?> ResolveThroughAsync(Query query, IPEndPoint server, string nsName, CancellationToken cancellationToken)
	{
		DnsExchange? reply = (await AskAsync(query, server, nsName, (int)DnsRecordType.A, cancellationToken)).Exchange;
		DnsRecord? a = reply?.Message.Answers.FirstOrDefault(record => record.Type == (int)DnsRecordType.A && !record.Malformed);
		return a is not null && IPAddress.TryParse(a.Data, out IPAddress? address) ? new IPEndPoint(address, DnsServerAddress.DefaultPort) : null;
	}

	private static DelegationWalkResult Finished(List<DelegationStep> steps, bool reachedAnswer, string summary) => new()
	{
		Steps = [.. steps],
		ReachedAnswer = reachedAnswer,
		Summary = summary,
	};

	private static string StepOutcome(DnsExchange exchange) => exchange.Message.Answers.Count > 0
		? $"answered with {Plural(exchange.Message.Answers.Count, "record")}"
		: exchange.Message.Authority.Count > 0 ? "referred onward" : "answered with no records";

	private static DelegationStepKind StepKind(DnsExchange exchange) => exchange.Message.ResponseCode != 0
		? DelegationStepKind.Failed
		: exchange.Message.Answers.Count > 0 ? DelegationStepKind.Answer : DelegationStepKind.NoData;

	private static string Plural(int count, string what) => count == 1 ? $"one {what}" : $"{count} {what}s";
}
