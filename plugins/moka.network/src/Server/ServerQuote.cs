using System.Text;

namespace Moka.Network.Server;

/// <summary>
/// The one way the session tool puts a word on a command line the server's shell reads. Mokaterm's own
/// <c>PosixShell</c> quoting is internal to the host, so this is the same rule written here: single quotes, inside
/// which nothing is special, and an embedded quote spelled as one that closes, an escaped quote and one that opens
/// again.
/// </summary>
public static class ServerQuote
{
	/// <summary>
	/// Quotes <paramref name="value"/> as one word for a POSIX shell. Spaces, <c>$</c>, backticks, glob characters and
	/// non-ASCII text pass through unchanged; a single quote becomes <c>'\''</c>.
	/// </summary>
	/// <exception cref="ArgumentException">The value holds a NUL character, which no command line can carry.</exception>
	public static string Quote(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (value.Contains('\0', StringComparison.Ordinal))
		{
			throw new ArgumentException("A shell word cannot hold a NUL character.", nameof(value));
		}

		return string.Concat("'", value.Replace("'", @"'\''", StringComparison.Ordinal), "'");
	}

	/// <summary>The words quoted and joined by single spaces, which is how every command here is assembled.</summary>
	public static string Line(IEnumerable<string> words)
	{
		ArgumentNullException.ThrowIfNull(words);
		StringBuilder builder = new();
		foreach (string word in words)
		{
			if (builder.Length > 0)
			{
				builder.Append(' ');
			}

			builder.Append(Quote(word));
		}

		return builder.ToString();
	}
}
