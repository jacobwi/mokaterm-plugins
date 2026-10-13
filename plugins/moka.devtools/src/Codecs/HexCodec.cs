using Moka.DevTools.Text;

namespace Moka.DevTools.Codecs;

/// <summary>
/// Hex both ways. Decoding takes the forms people paste: spaces, colons, dashes or commas between bytes, and a 0x or \x
/// before each one or before the whole run.
/// </summary>
public static class HexCodec
{
	public static string Encode(ReadOnlySpan<byte> data, bool upper, HexSeparator separator) => HexText.Format(data, upper, separator switch
	{
		HexSeparator.Space => " ",
		HexSeparator.Colon => ":",
		_ => "",
	});

	public static byte[] Decode(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		List<byte> output = new(text.Length / 2);
		int high = -1;
		int highAt = -1;
		for (int index = 0; index < text.Length; index++)
		{
			char c = text[index];
			if (char.IsWhiteSpace(c) || c is ':' or '-' or ',')
			{
				if (high >= 0)
				{
					throw new CodecException($"The byte at {Base64Codec.Place(highAt)} has one hex digit; each byte needs two.", highAt);
				}

				continue;
			}

			// A prefix only counts between bytes, so the 0 of "0a" is never taken for one.
			if (high < 0 && index + 1 < text.Length && (c == '0' || c == '\\') && text[index + 1] is 'x' or 'X')
			{
				index++;
				continue;
			}

			int value = UrlCodec.HexValue(c);
			if (value < 0)
			{
				throw new CodecException($"{Base64Codec.Describe(c)} at {Base64Codec.Place(index)} is not a hex digit.", index);
			}

			if (high < 0)
			{
				high = value;
				highAt = index;
			}
			else
			{
				output.Add((byte)((high << 4) | value));
				high = -1;
			}
		}

		if (high >= 0)
		{
			throw new CodecException($"The last byte, at {Base64Codec.Place(highAt)}, has one hex digit; each byte needs two.", highAt);
		}

		return [.. output];
	}
}
