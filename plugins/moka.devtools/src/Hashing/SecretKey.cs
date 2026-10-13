using Moka.DevTools.Codecs;
using Moka.DevTools.Text;

namespace Moka.DevTools.Hashing;

/// <summary>
/// Reads an HMAC key or a JWT secret into bytes. The caller owns the array and clears it once the MAC is made; the
/// messages never quote the key.
/// </summary>
public static class SecretKey
{
	public static byte[] Read(string text, KeyFormat format)
	{
		ArgumentNullException.ThrowIfNull(text);
		try
		{
			return format switch
			{
				KeyFormat.Hex => HexCodec.Decode(text),
				KeyFormat.Base64 => Base64Codec.Decode(text, urlSafe: text.Contains('-', StringComparison.Ordinal) || text.Contains('_', StringComparison.Ordinal)).Bytes,
				_ => StrictUtf8.Encode(text),
			};
		}
		catch (CodecException ex)
		{
			// The codec's message quotes the offending character; for a secret only its place is said.
			throw new FormatException(ex.Position is { } position
				? $"The key is not {Name(format)}: the character at position {position + 1} does not belong."
				: $"The key is not {Name(format)}.");
		}
	}

	private static string Name(KeyFormat format) => format switch
	{
		KeyFormat.Hex => "hex",
		KeyFormat.Base64 => "Base64",
		_ => "text",
	};
}
