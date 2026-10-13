using System.Buffers;
using System.Globalization;
using System.Text;
using Moka.DevTools.Text;

namespace Moka.DevTools.Codecs;

/// <summary>
/// Percent encoding over UTF-8. A component keeps only RFC 3986's unreserved characters; a whole URL also keeps the
/// characters that give it its structure, as JavaScript's encodeURI and decodeURI do, so an escaped slash or ampersand
/// in a query value stays escaped when the whole address is decoded.
/// </summary>
public static class UrlCodec
{
	private static readonly SearchValues<char> Unreserved =
		SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~");

	private static readonly SearchValues<char> UrlKept =
		SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~;,/?:@&=+$!*'()#");

	// What decodeURI leaves escaped: decoding one of these would change what the address means.
	private static readonly SearchValues<char> UrlReserved = SearchValues.Create(";/?:@&=+$,#");

	public static string Encode(string text, UrlMode mode)
	{
		ArgumentNullException.ThrowIfNull(text);
		SearchValues<char> kept = mode == UrlMode.Component ? Unreserved : UrlKept;
		byte[] bytes = Utf8Of(text);
		StringBuilder output = new(bytes.Length * 2);
		foreach (byte value in bytes)
		{
			if (value < 0x80 && kept.Contains((char)value))
			{
				output.Append((char)value);
			}
			else
			{
				output.Append('%').Append(value.ToString("X2", CultureInfo.InvariantCulture));
			}
		}

		return output.ToString();
	}

	/// <summary>The bytes the text stands for; the caller shows them as text only when they are UTF-8.</summary>
	public static byte[] Decode(string text, UrlMode mode, bool plusIsSpace)
	{
		ArgumentNullException.ThrowIfNull(text);
		List<byte> output = new(text.Length);
		Span<byte> scratch = stackalloc byte[4];
		for (int index = 0; index < text.Length; index++)
		{
			char c = text[index];
			if (c == '%')
			{
				if (index + 2 >= text.Length || HexValue(text[index + 1]) < 0 || HexValue(text[index + 2]) < 0)
				{
					throw new CodecException($"The % at {Base64Codec.Place(index)} is not followed by two hex digits.", index);
				}

				byte value = (byte)((HexValue(text[index + 1]) << 4) | HexValue(text[index + 2]));
				if (mode == UrlMode.WholeUrl && value < 0x80 && UrlReserved.Contains((char)value))
				{
					output.Add((byte)'%');
					output.Add((byte)text[index + 1]);
					output.Add((byte)text[index + 2]);
				}
				else
				{
					output.Add(value);
				}

				index += 2;
			}
			else if (c == '+' && plusIsSpace)
			{
				output.Add((byte)' ');
			}
			else
			{
				int length = char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
				if (char.IsSurrogate(c) && length == 1)
				{
					throw new CodecException($"Half of a character pair at {Base64Codec.Place(index)} cannot be written as UTF-8.", index);
				}

				int written = Encoding.UTF8.GetBytes(text.AsSpan(index, length), scratch);
				for (int at = 0; at < written; at++)
				{
					output.Add(scratch[at]);
				}

				index += length - 1;
			}
		}

		return [.. output];
	}

	internal static int HexValue(char c) => c switch
	{
		>= '0' and <= '9' => c - '0',
		>= 'a' and <= 'f' => c - 'a' + 10,
		>= 'A' and <= 'F' => c - 'A' + 10,
		_ => -1,
	};

	private static byte[] Utf8Of(string text)
	{
		try
		{
			return StrictUtf8.Encode(text);
		}
		catch (FormatException ex)
		{
			throw new CodecException(ex.Message);
		}
	}
}
