using System.Diagnostics;
using Moka.Network.Targets;

namespace Moka.Network.Whois;

/// <summary>
/// Asks IANA first, which knows every top level domain's and every address block's registry, then follows the
/// referrals in the answers, at most <see cref="MaxReferrals"/> of them and never to a server already asked. All
/// answers together are held to <see cref="MaxTotalBytes"/>.
/// </summary>
public static class WhoisClient
{
	public const string RootServer = "whois.iana.org";

	public const int MaxReferrals = 2;

	public const int MaxTotalBytes = 1024 * 1024;

	public static async Task<WhoisResult> LookupAsync(IWhoisTransport transport, string query, TimeSpan timeout, Action<WhoisAnswer> onAnswer, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(transport);
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(onAnswer);
		List<WhoisAnswer> answers = [];
		HashSet<string> asked = new(StringComparer.OrdinalIgnoreCase);
		string server = RootServer;
		int remaining = MaxTotalBytes;
		for (int hop = 0; ; hop++)
		{
			asked.Add(server);
			long started = Stopwatch.GetTimestamp();
			WhoisAnswer answer;
			try
			{
				WhoisRaw raw = await transport.AskAsync(server, query, remaining, timeout, cancellationToken);
				string text = PlainText.FromBytes(raw.Bytes);
				remaining -= raw.Bytes.Length;
				answer = new WhoisAnswer
				{
					Server = server,
					Text = text,
					Bytes = raw.Bytes.Length,
					Elapsed = Stopwatch.GetElapsedTime(started),
					Cut = raw.Cut,
					TimedOut = raw.TimedOut,
					Referral = Referral(text, server),
				};
			}
			catch (ProbeException ex) when (answers.Count > 0)
			{
				// The registry's answer already stands; the referral that failed is shown with its reason beside it.
				answer = new WhoisAnswer { Server = server, Text = "", Bytes = 0, Elapsed = Stopwatch.GetElapsedTime(started), Error = ex.Message };
				answers.Add(answer);
				onAnswer(answer);
				return new WhoisResult(answers, null);
			}

			answers.Add(answer);
			onAnswer(answer);

			if (answer.Referral is not { } next || asked.Contains(next))
			{
				return new WhoisResult(answers, null);
			}

			if (hop >= MaxReferrals)
			{
				return new WhoisResult(answers, $"{server} refers to {next}, which was not asked: at most {MaxReferrals} referrals are followed.");
			}

			if (remaining <= 0)
			{
				return new WhoisResult(answers, $"{server} refers to {next}, which was not asked: the answers so far reached the {MaxTotalBytes / 1024} KiB limit.");
			}

			server = next;
		}
	}

	/// <summary>The server an answer sends the question on to; a registrar naming its own server refers nowhere.</summary>
	private static string? Referral(string text, string server) =>
		WhoisReferral.Find(text) is { } found && !string.Equals(found, server, StringComparison.OrdinalIgnoreCase) ? found : null;
}
