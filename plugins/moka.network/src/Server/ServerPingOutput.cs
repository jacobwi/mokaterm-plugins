using System.Globalization;
using System.Text.RegularExpressions;

namespace Moka.Network.Server;

/// <summary>One reply line of a ping the server ran: its sequence number and round trip.</summary>
public sealed record ServerPingReply(int Sequence, double Milliseconds);

/// <summary>
/// What the Reach tab reads back out of a ping's text: each reply's round trip, and the counts from the summary line
/// when the server printed one. The text itself stays the answer; this only gives the chart and the tiles numbers.
/// </summary>
public sealed record ServerPingResult(IReadOnlyList<ServerPingReply> Replies, int? Transmitted, int? Received)
{
	public static ServerPingResult Empty { get; } = new([], null, null);

	/// <summary>Requests sent, from the summary when there is one, otherwise the replies that were seen.</summary>
	public int Sent => Transmitted ?? Replies.Count;

	public int Answered => Received ?? Replies.Count;

	public double LossPercent => Sent == 0 ? 0 : 100.0 * Math.Max(0, Sent - Answered) / Sent;

	public double? Minimum => Replies.Count > 0 ? Replies.Min(reply => reply.Milliseconds) : null;

	public double? Average => Replies.Count > 0 ? Replies.Average(reply => reply.Milliseconds) : null;

	public double? Maximum => Replies.Count > 0 ? Replies.Max(reply => reply.Milliseconds) : null;
}

/// <summary>
/// Reads iputils and BusyBox ping output: <c>icmp_seq=3 ttl=56 time=11.6 ms</c> or <c>seq=2 ttl=57 time=12.345 ms</c>
/// per reply, and <c>4 packets transmitted, 3 received</c> (BusyBox: <c>3 packets received</c>) at the end. Lines it
/// does not know, such as errors, are passed over.
/// </summary>
public static partial class ServerPingOutput
{
	public static ServerPingResult Parse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		List<ServerPingReply> replies = [];
		int? transmitted = null;
		int? received = null;
		foreach (string line in text.Split('\n'))
		{
			if (ReplyLine().Match(line) is { Success: true } reply
				&& int.TryParse(reply.Groups["seq"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int sequence)
				&& double.TryParse(reply.Groups["ms"].ValueSpan, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double milliseconds))
			{
				replies.Add(new ServerPingReply(sequence, milliseconds));
				continue;
			}

			if (SummaryLine().Match(line) is { Success: true } summary
				&& int.TryParse(summary.Groups["sent"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int sent)
				&& int.TryParse(summary.Groups["received"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int got))
			{
				transmitted = sent;
				received = got;
			}
		}

		return new ServerPingResult(replies, transmitted, received);
	}

	[GeneratedRegex(@"\b(?:icmp_seq|seq)=(?<seq>\d+)\b.*?\btime[=<](?<ms>\d+(?:\.\d+)?) ?ms\b", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ReplyLine();

	[GeneratedRegex(@"\b(?<sent>\d+) packets transmitted, (?<received>\d+) (?:packets )?received\b", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex SummaryLine();
}
