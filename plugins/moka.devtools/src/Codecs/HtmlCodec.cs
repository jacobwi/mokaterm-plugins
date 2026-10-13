using System.Globalization;
using System.Net;
using System.Text;

namespace Moka.DevTools.Codecs;

/// <summary>
/// HTML character references. Encoding escapes the five characters that matter in markup and, when asked, everything
/// outside ASCII. Decoding reads numeric references and the named ones .NET knows (HTML 4 and &amp;apos;), and stops at a
/// named reference it does not know rather than leaving it in the text as if it had been decoded.
/// </summary>
public static class HtmlCodec
{
	private const int MaxNameLength = 32;

	public static string Encode(string text, bool encodeNonAscii)
	{
		ArgumentNullException.ThrowIfNull(text);
		StringBuilder output = new(text.Length + 16);
		for (int index = 0; index < text.Length; index++)
		{
			char c = text[index];
			switch (c)
			{
				case '&':
					output.Append("&amp;");
					break;
				case '<':
					output.Append("&lt;");
					break;
				case '>':
					output.Append("&gt;");
					break;
				case '"':
					output.Append("&quot;");
					break;
				case '\'':
					output.Append("&#39;");
					break;
				default:
					if (encodeNonAscii && c >= 0x80)
					{
						int codePoint = c;
						if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
						{
							codePoint = char.ConvertToUtf32(c, text[index + 1]);
							index++;
						}
						else if (char.IsSurrogate(c))
						{
							throw new CodecException($"Half of a character pair at {Base64Codec.Place(index)} has no character reference.", index);
						}

						output.Append("&#x").Append(codePoint.ToString("X", CultureInfo.InvariantCulture)).Append(';');
					}
					else
					{
						output.Append(c);
					}

					break;
			}
		}

		return output.ToString();
	}

	/// <summary>The decoded text, and a note when a numeric reference had no closing semicolon (browsers read those too).</summary>
	public static (string Text, string? Note) Decode(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		StringBuilder output = new(text.Length);
		bool unterminated = false;
		int index = 0;
		while (index < text.Length)
		{
			char c = text[index];
			if (c != '&')
			{
				output.Append(c);
				index++;
				continue;
			}

			if (index + 1 < text.Length && text[index + 1] == '#')
			{
				index = ReadNumeric(text, index, output, ref unterminated);
				continue;
			}

			int end = index + 1;
			while (end < text.Length && end - index <= MaxNameLength && char.IsAsciiLetterOrDigit(text[end]))
			{
				end++;
			}

			// "a & b" or "&copy" without a semicolon: markup reads both as plain text, so they stay as written.
			if (end == index + 1 || end >= text.Length || text[end] != ';')
			{
				output.Append('&');
				index++;
				continue;
			}

			string reference = text[index..(end + 1)];
			string decoded = WebUtility.HtmlDecode(reference);
			if (decoded == reference)
			{
				throw new CodecException($"{reference} at {Base64Codec.Place(index)} is not an entity this decoder knows.", index);
			}

			output.Append(decoded);
			index = end + 1;
		}

		return (output.ToString(), unterminated ? "Some numeric references had no closing semicolon. Browsers read them anyway, and so did this." : null);
	}

	private static int ReadNumeric(string text, int start, StringBuilder output, ref bool unterminated)
	{
		int at = start + 2;
		bool hex = at < text.Length && text[at] is 'x' or 'X';
		if (hex)
		{
			at++;
		}

		int digitsStart = at;
		while (at < text.Length && (hex ? char.IsAsciiHexDigit(text[at]) : char.IsAsciiDigit(text[at])) && at - digitsStart < 8)
		{
			at++;
		}

		if (at == digitsStart)
		{
			// "&#" with no digits is not a reference, so markup shows it as written.
			output.Append(text, start, at - start);
			return at;
		}

		if (at < text.Length && (hex ? char.IsAsciiHexDigit(text[at]) : char.IsAsciiDigit(text[at])))
		{
			throw new CodecException($"The reference at {Base64Codec.Place(start)} is too large to be a character.", start);
		}

		long value = long.Parse(text.AsSpan(digitsStart, at - digitsStart), hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture);
		if (value is 0 or > 0x10FFFF || (value is >= 0xD800 and <= 0xDFFF))
		{
			throw new CodecException($"{text[start..Math.Min(text.Length, at + 1)]} at {Base64Codec.Place(start)} is not a character.", start);
		}

		output.Append(char.ConvertFromUtf32((int)value));
		if (at < text.Length && text[at] == ';')
		{
			return at + 1;
		}

		unterminated = true;
		return at;
	}
}
