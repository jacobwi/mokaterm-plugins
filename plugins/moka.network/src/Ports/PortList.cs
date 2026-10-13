using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Moka.Network.Ports;

/// <summary>
/// The ports box: one port or a short list, by number or by a common service name (<c>22, 443, rdp</c>). Ranges are
/// refused outright, and so is a list longer than <see cref="MaxPorts"/>: this checks known services, it does not scan.
/// </summary>
public static class PortList
{
	public const int MaxPorts = 10;

	private static readonly Dictionary<string, int> Services = new(StringComparer.OrdinalIgnoreCase)
	{
		["ftp"] = 21,
		["ssh"] = 22,
		["telnet"] = 23,
		["smtp"] = 25,
		["dns"] = 53,
		["http"] = 80,
		["pop3"] = 110,
		["imap"] = 143,
		["https"] = 443,
		["smb"] = 445,
		["submission"] = 587,
		["imaps"] = 993,
		["pop3s"] = 995,
		["mqtt"] = 1883,
		["mysql"] = 3306,
		["rdp"] = 3389,
		["postgres"] = 5432,
		["vnc"] = 5900,
		["redis"] = 6379,
		["mqtts"] = 8883,
	};

	private static readonly char[] Separators = [',', ' ', ';', '\t'];

	public static bool TryParse(string? text, [NotNullWhen(true)] out IReadOnlyList<int>? ports, [NotNullWhen(false)] out string? problem)
	{
		ports = null;
		string input = (text ?? "").Trim();
		if (input.Length == 0)
		{
			problem = "Type a port, such as 22, or a few separated by commas.";
			return false;
		}

		if (input.Contains('-', StringComparison.Ordinal) || input.Contains(':', StringComparison.Ordinal))
		{
			problem = $"No ranges: this checks up to {MaxPorts} ports by number, it is not a port scanner.";
			return false;
		}

		List<int> found = [];
		foreach (string part in input.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
		{
			int port;
			if (Services.TryGetValue(part, out int known))
			{
				port = known;
			}
			else if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
			{
				problem = $"{Shorten(part)} is not a port. Ports run from 1 to 65535.";
				return false;
			}

			if (!found.Contains(port))
			{
				found.Add(port);
			}

			if (found.Count > MaxPorts)
			{
				problem = $"At most {MaxPorts} ports at a time.";
				return false;
			}
		}

		ports = found;
		problem = null;
		return true;
	}

	/// <summary>The service a port usually carries, for the row's label, or null.</summary>
	public static string? ServiceName(int port) => Services.FirstOrDefault(pair => pair.Value == port).Key;

	private static string Shorten(string part) => part.Length > 16 ? part[..16] + "..." : part;
}
