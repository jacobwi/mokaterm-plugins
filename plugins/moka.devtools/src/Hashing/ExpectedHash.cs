using Moka.DevTools.Codecs;

namespace Moka.DevTools.Hashing;

/// <summary>
/// A hash pasted to compare against: hex in any case, or Base64 or Base64url. It takes the forms hashes are passed
/// around in: a line of sha256sum output (the hash and then the file name), an algorithm prefix ("sha256:") or a
/// subresource integrity value ("sha384-...").
/// </summary>
internal static class ExpectedHash
{
	/// <summary>The shortest hash offered is CRC32, four bytes.</summary>
	private const int MinBytes = 4;

	private const int MinHexDigits = MinBytes * 2;

	private static readonly string[] Prefixes =
		["md5", "sha1", "sha-1", "sha256", "sha-256", "sha384", "sha-384", "sha512", "sha-512", "sha3-256", "sha3-384", "sha3-512", "crc32"];

	/// <summary>The bytes and the form they were read in ("hex", "Base64"). Throws <see cref="FormatException"/> with a reason.</summary>
	public static (byte[] Bytes, string Form) Parse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		string value = text.Trim();

		// A hash then a file name, as sha256sum prints them; a short first word is not a hash, so the text stays whole.
		int space = value.IndexOfAny([' ', '\t', '\r', '\n']);
		if (space >= MinHexDigits)
		{
			value = value[..space];
		}

		foreach (string prefix in Prefixes)
		{
			if (value.Length > prefix.Length + 1
				&& value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
				&& value[prefix.Length] is ':' or '-')
			{
				value = value[(prefix.Length + 1)..];
				break;
			}
		}

		if (value.Length == 0)
		{
			throw new FormatException("Paste the hash to compare with.");
		}

		// Only hex digits is a hex hash, even when it would also read as Base64; an odd count is one with a digit lost.
		if (value.All(char.IsAsciiHexDigit))
		{
			return value.Length % 2 == 0
				? (Convert.FromHexString(value), "hex")
				: throw new FormatException("That has an odd number of hex digits, so it is not a whole hash.");
		}

		bool urlSafe = value.Contains('-', StringComparison.Ordinal) || value.Contains('_', StringComparison.Ordinal);
		byte[] decoded;
		try
		{
			decoded = Base64Codec.Decode(value, urlSafe).Bytes;
		}
		catch (CodecException)
		{
			throw new FormatException("That is neither hex nor Base64.");
		}

		return decoded.Length >= MinBytes
			? (decoded, urlSafe ? "Base64url" : "Base64")
			: throw new FormatException("That is too short to be a hash.");
	}
}
