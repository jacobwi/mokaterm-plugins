namespace Moka.Network.Server;

/// <summary>
/// One listening socket as <c>ss -tulpn</c> prints it: <see cref="Netid"/> (<c>tcp</c> or <c>udp</c>), the state word
/// ss itself uses, the local end in address:port form as it printed it, and the processes holding it, which ss only
/// names for sockets the logged-in account can see.
/// </summary>
/// <param name="Process">The names of the processes holding the socket, joined, or "" when ss printed none.</param>
/// <param name="ProcessDetail">The whole <c>users:(...)</c> column as ss printed it, for a tooltip.</param>
public sealed record SsRow(string Netid, string State, string Local, string Process, string ProcessDetail);

/// <summary>
/// Reads the listing <c>ss -tulpn</c> prints on the server. The header line and any line that does not hold the six
/// columns ss promises are skipped, and the process column, which ss leaves out entirely for sockets the account
/// cannot look into, is read when there and left empty when not, exactly as the server told it.
/// </summary>
public static class SsListing
{
	public static IReadOnlyList<SsRow> Parse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		List<SsRow> rows = [];
		foreach (string line in text.Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.Length == 0)
			{
				continue;
			}

			string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length < 6 || parts[0] == "Netid")
			{
				continue;
			}

			string detail = parts.Length > 6 ? string.Join(' ', parts[6..]) : "";
			rows.Add(new SsRow(parts[0], parts[1], parts[4], ProcessNames(detail), detail));
		}

		return rows;
	}

	/// <summary>
	/// The names out of <c>users:(("nginx",pid=900,fd=6),("nginx",pid=901,fd=6))</c>, each once, in order: nginx.
	/// A process name may hold spaces, which is why the column is taken whole before this reads it.
	/// </summary>
	internal static string ProcessNames(string detail)
	{
		if (detail.Length == 0)
		{
			return "";
		}

		List<string> names = [];
		int search = 0;
		while (search < detail.Length && detail.IndexOf("(\"", search, StringComparison.Ordinal) is int open && open >= 0)
		{
			int nameStart = open + 2;
			int close = detail.IndexOf('"', nameStart);
			if (close < 0)
			{
				break;
			}

			string name = detail[nameStart..close];
			if (name.Length > 0 && !names.Contains(name, StringComparer.Ordinal))
			{
				names.Add(name);
			}

			search = close + 1;
		}

		return string.Join(", ", names);
	}
}
