using System.Text;
using Mokaterm.Abstractions.Terminal;

namespace Moka.Network.Targets;

/// <summary>
/// Bytes a server sent, made fit to show: decoded, escape sequences and control characters out through
/// <see cref="TerminalTextFilter.Plain"/>, and the Unicode direction overrides out as well, which would otherwise let a
/// server make text read differently from what it is.
/// </summary>
public static class PlainText
{
	private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	/// <summary>UTF-8 when the bytes are valid UTF-8, otherwise Latin-1, which every byte decodes in, as older whois servers write.</summary>
	public static string FromBytes(ReadOnlySpan<byte> bytes)
	{
		string text;
		try
		{
			text = StrictUtf8.GetString(bytes);
		}
		catch (DecoderFallbackException)
		{
			text = Encoding.Latin1.GetString(bytes);
		}

		return Clean(text);
	}

	public static string Clean(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		string plain = TerminalTextFilter.Plain(text);
		if (!plain.Any(IsDirectionControl))
		{
			return plain;
		}

		StringBuilder kept = new(plain.Length);
		foreach (char c in plain)
		{
			if (!IsDirectionControl(c))
			{
				kept.Append(c);
			}
		}

		return kept.ToString();
	}

	private static bool IsDirectionControl(char c) => c is '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');
}
