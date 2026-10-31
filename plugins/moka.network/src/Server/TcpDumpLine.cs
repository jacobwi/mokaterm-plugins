using System.Net;
using System.Net.Sockets;

namespace Moka.Network.Server;

/// <summary>
/// One packet line of <c>tcpdump -nn -l</c>, split into the columns the capture tab shows. The parser is tolerant by
/// design: tcpdump writes dozens of per-protocol summaries, so anything it cannot name lands whole in
/// <see cref="Info"/> with the rest empty rather than being lost or throwing, and a line is never rejected.
/// </summary>
public sealed record TcpDumpLine
{
	/// <summary>The time tcpdump printed, <c>hh:mm:ss.frac</c>, as it printed it.</summary>
	public string Time { get; init; } = "";

	/// <summary>The sending end in address:port form, or the bare address for protocols without ports.</summary>
	public string Source { get; init; } = "";

	/// <summary>The receiving end, in the same form as <see cref="Source"/>.</summary>
	public string Destination { get; init; } = "";

	/// <summary>IP, TCP, UDP, ICMP, ICMP6, ARP and neighbours, or "" when the line did not say.</summary>
	public string Protocol { get; init; } = "";

	/// <summary>The payload length as a plain number of bytes, or "" when the line did not say.</summary>
	public string Length { get; init; } = "";

	/// <summary>What tcpdump wrote after the ends: flags, sequences, the DNS question, the ARP request.</summary>
	public string Info { get; init; } = "";

	public static TcpDumpLine Parse(string line)
	{
		ArgumentNullException.ThrowIfNull(line);
		string rest = line.Trim();
		if (rest.Length == 0)
		{
			return new TcpDumpLine();
		}

		string time = TakeTime(ref rest);
		if (rest.StartsWith("IP6 ", StringComparison.Ordinal))
		{
			return FromIp(time, rest["IP6 ".Length..].TrimStart(), "IP6");
		}

		if (rest.StartsWith("IP ", StringComparison.Ordinal))
		{
			return FromIp(time, rest["IP ".Length..].TrimStart(), "IP");
		}

		// ARP, STP and the other non-IP neighbours: no ends to split, so the line itself is the info.
		string[] parts = rest.Split(' ', 2);
		return new TcpDumpLine
		{
			Time = time,
			Protocol = parts[0].TrimEnd(','),
			Info = parts.Length > 1 ? parts[1].Trim() : "",
		};
	}

	/// <summary>Takes a leading <c>hh:mm:ss.frac</c> off the line when there is one, leaving the rest after its space.</summary>
	private static string TakeTime(ref string rest)
	{
		int end = 0;
		while (end < rest.Length && (char.IsAsciiDigit(rest[end]) || rest[end] is ':' or '.'))
		{
			end++;
		}

		if (end == 0 || end >= rest.Length || rest[end] != ' ' || !rest.AsSpan(0, end).Contains(':'))
		{
			return "";
		}

		string time = rest[..end];
		rest = rest[(end + 1)..];
		return time;
	}

	private static TcpDumpLine FromIp(string time, string body, string marker)
	{
		int arrow = body.IndexOf(" > ", StringComparison.Ordinal);
		if (arrow < 0)
		{
			return new TcpDumpLine { Time = time, Protocol = marker, Info = body };
		}

		string source = body[..arrow].Trim();
		string after = body[(arrow + 3)..];
		int colon = after.IndexOf(": ", StringComparison.Ordinal);
		string destination = (colon < 0 ? after : after[..colon]).Trim();
		string info = colon < 0 ? "" : after[(colon + 2)..];
		return new TcpDumpLine
		{
			Time = time,
			Source = Endpoint(source),
			Destination = Endpoint(destination),
			Protocol = ProtocolOf(info),
			Length = LengthOf(info),
			Info = info,
		};
	}

	/// <summary>
	/// tcpdump writes ends as <c>10.0.0.5.52344</c> and <c>2001:db8::1.443</c>, the port after the last dot; a bare
	/// address, as icmp prints, has no port at all. IPv6 gets brackets back so a port cannot be mistaken for part of
	/// the address, and anything that reads as neither passes through as it came.
	/// </summary>
	internal static string Endpoint(string endpoint)
	{
		if (IPAddress.TryParse(endpoint, out _))
		{
			return endpoint;
		}

		int split = endpoint.LastIndexOf('.');
		if (split > 0
			&& endpoint[(split + 1)..].Length > 0
			&& endpoint[(split + 1)..].All(char.IsAsciiDigit)
			&& IPAddress.TryParse(endpoint[..split], out IPAddress? address))
		{
			string host = endpoint[..split];
			string port = endpoint[(split + 1)..];
			return address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{host}]:{port}" : $"{host}:{port}";
		}

		return endpoint;
	}

	/// <summary>Names the protocol from how tcpdump starts its summary: flags mean tcp, and so on.</summary>
	internal static string ProtocolOf(string info)
	{
		if (info.StartsWith("Flags", StringComparison.Ordinal))
		{
			return "TCP";
		}

		if (info.StartsWith("UDP", StringComparison.Ordinal))
		{
			return "UDP";
		}

		if (info.StartsWith("icmp6", StringComparison.OrdinalIgnoreCase))
		{
			return "ICMP6";
		}

		if (info.StartsWith("icmp", StringComparison.OrdinalIgnoreCase))
		{
			return "ICMP";
		}

		return "";
	}

	/// <summary>The number after <c>length</c>, or the byte count icmp writes before its colon.</summary>
	internal static string LengthOf(string info)
	{
		int at = info.IndexOf("length ", StringComparison.Ordinal);
		if (at >= 0)
		{
			int start = at + "length ".Length;
			int end = start;
			while (end < info.Length && char.IsAsciiDigit(info[end]))
			{
				end++;
			}

			if (end > start)
			{
				return info[start..end];
			}
		}

		if (ProtocolOf(info) is "ICMP" or "ICMP6")
		{
			// "ICMP echo request, id 1, seq 1, length 64" carries length too, but very old prints wrote "icmp 64: ...".
			int start = 0;
			while (start < info.Length && !char.IsAsciiDigit(info[start]))
			{
				start++;
			}

			int end = start;
			while (end < info.Length && char.IsAsciiDigit(info[end]))
			{
				end++;
			}

			if (end > start && end < info.Length && info[end] == ':')
			{
				return info[start..end];
			}
		}

		return "";
	}
}
