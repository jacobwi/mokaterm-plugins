using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Moka.Certificates.Crypto;

/// <summary>
/// Bytes as text and back: base64 and hex for signatures and ciphertext, and the colon-separated hex that fingerprints and
/// key identifiers are shown in. Reading takes either form, with whitespace, line breaks and colons ignored.
/// </summary>
public static class BinaryText
{
	public static string Format(ReadOnlySpan<byte> bytes, BinaryEncoding encoding) => encoding switch
	{
		BinaryEncoding.Hex => Convert.ToHexStringLower(bytes),
		_ => Convert.ToBase64String(bytes),
	};

	/// <summary>Upper-case hex pairs joined by colons, the way fingerprints are written: <c>AB:CD:01</c>.</summary>
	public static string Colons(ReadOnlySpan<byte> bytes)
	{
		if (bytes.IsEmpty)
		{
			return "";
		}

		StringBuilder builder = new(bytes.Length * 3);
		for (int index = 0; index < bytes.Length; index++)
		{
			if (index > 0)
			{
				builder.Append(':');
			}

			builder.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
		}

		return builder.ToString();
	}

	/// <summary>
	/// Reads base64 or hex. Hex wins when the text could be either and has an even number of hex digits only, which is
	/// how a hex dump of a signature looks; anything with base64's other characters is base64.
	/// </summary>
	public static bool TryParse(string? text, [NotNullWhen(true)] out byte[]? bytes)
	{
		bytes = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string compact = Compact(text, dropColons: true);
		if (compact.Length > 0 && compact.Length % 2 == 0 && compact.All(char.IsAsciiHexDigit))
		{
			bytes = Convert.FromHexString(compact);
			return true;
		}

		return TryParseBase64(text, out bytes);
	}

	/// <summary>Base64, with line breaks and spaces ignored, as PEM bodies and pasted signatures carry them.</summary>
	public static bool TryParseBase64(string? text, [NotNullWhen(true)] out byte[]? bytes)
	{
		bytes = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string compact = Compact(text, dropColons: false);
		if (compact.Length == 0 || compact.Length % 4 != 0)
		{
			return false;
		}

		byte[] buffer = new byte[compact.Length / 4 * 3];
		if (!Convert.TryFromBase64String(compact, buffer, out int written))
		{
			return false;
		}

		bytes = buffer.AsSpan(0, written).ToArray();
		return true;
	}

	/// <summary>True when <paramref name="text"/> is base64 and nothing else, whitespace aside.</summary>
	public static bool IsBase64(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		int count = 0;
		foreach (char c in text)
		{
			if (char.IsWhiteSpace(c))
			{
				continue;
			}

			if (!char.IsAsciiLetterOrDigit(c) && c is not '+' and not '/' and not '=')
			{
				return false;
			}

			count++;
		}

		return count > 0 && count % 4 == 0;
	}

	private static string Compact(string text, bool dropColons)
	{
		StringBuilder builder = new(text.Length);
		foreach (char c in text)
		{
			if (char.IsWhiteSpace(c) || (dropColons && c == ':'))
			{
				continue;
			}

			builder.Append(c);
		}

		return builder.ToString();
	}
}
