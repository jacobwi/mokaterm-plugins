using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Moka.Network.Server;

/// <summary>One hop of a trace the server ran: its number, the first address that answered, and its best round trip.</summary>
public sealed record ServerTraceHop(int Hop, string? Address, double? Milliseconds);

/// <summary>
/// Reads the hops out of <c>tracepath -n</c> and <c>traceroute -n</c> output: a line that starts with the hop number
/// (tracepath writes <c>1:</c>, traceroute <c>1</c>), then an address and times such as <c>0.345ms</c> or
/// <c>0.345 ms</c>. tracepath can print a hop on several lines, so lines of one hop are merged and its fastest time
/// kept. Header, <c>[LOCALHOST]</c> and resume lines carry no address and no time, and are passed over.
/// </summary>
public static partial class ServerTraceOutput
{
	public static IReadOnlyList<ServerTraceHop> Parse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		SortedDictionary<int, ServerTraceHop> hops = [];
		foreach (string line in text.Split('\n'))
		{
			Match hopLine = HopLine().Match(line);
			if (!hopLine.Success || !int.TryParse(hopLine.Groups["hop"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int hop))
			{
				continue;
			}

			string rest = hopLine.Groups["rest"].Value;
			if (rest.Contains("[LOCALHOST]", StringComparison.Ordinal))
			{
				continue;
			}

			string? address = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(word => word.Trim('(', ')'))
				.FirstOrDefault(word => IPAddress.TryParse(word, out _));
			double? best = null;
			foreach (Match time in Time().Matches(rest))
			{
				if (double.TryParse(time.Groups["ms"].ValueSpan, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double milliseconds))
				{
					best = best is { } known ? Math.Min(known, milliseconds) : milliseconds;
				}
			}

			hops[hop] = hops.TryGetValue(hop, out ServerTraceHop? seen)
				? new ServerTraceHop(hop, seen.Address ?? address, Min(seen.Milliseconds, best))
				: new ServerTraceHop(hop, address, best);
		}

		return [.. hops.Values];
	}

	private static double? Min(double? first, double? second) => (first, second) switch
	{
		({ } a, { } b) => Math.Min(a, b),
		_ => first ?? second,
	};

	[GeneratedRegex(@"^\s*(?<hop>\d{1,3})\??:?\s+(?<rest>.*)$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex HopLine();

	[GeneratedRegex(@"(?<ms>\d+(?:\.\d+)?) ?ms\b", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex Time();
}
