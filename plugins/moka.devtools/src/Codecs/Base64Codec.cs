using System.Globalization;

namespace Moka.DevTools.Codecs;

/// <summary>
/// Base64 and Base64url (RFC 4648), with or without padding. Decoding skips whitespace, so a wrapped block reads, and
/// stops at the first character that does not belong, naming it and its place.
/// </summary>
public static class Base64Codec
{
	public static string Encode(ReadOnlySpan<byte> data, bool urlSafe, bool padding)
	{
		string text = Convert.ToBase64String(data);
		if (urlSafe)
		{
			text = text.Replace('+', '-').Replace('/', '_');
		}

		return padding ? text : text.TrimEnd('=');
	}

	/// <summary>The bytes, and a note when the text was read in a way worth knowing about.</summary>
	public static (byte[] Bytes, string? Note) Decode(string text, bool urlSafe)
	{
		ArgumentNullException.ThrowIfNull(text);
		byte[] output = new byte[(text.Length * 3 / 4) + 3];
		int written = 0;
		int accumulator = 0;
		int bits = 0;
		int symbols = 0;
		int padding = 0;
		int lastSymbol = -1;
		for (int index = 0; index < text.Length; index++)
		{
			char c = text[index];
			if (char.IsWhiteSpace(c))
			{
				continue;
			}

			if (c == '=')
			{
				padding++;
				continue;
			}

			if (padding > 0)
			{
				throw new CodecException($"Padding (=) can only come at the end, but {Describe(c)} follows it at {Place(index)}.", index);
			}

			int value = ValueOf(c, urlSafe);
			if (value < 0)
			{
				throw new CodecException(NotBase64(c, index, urlSafe), index);
			}

			accumulator = ((accumulator << 6) | value) & 0xFFFFFF;
			bits += 6;
			symbols++;
			lastSymbol = index;
			if (bits >= 8)
			{
				bits -= 8;
				output[written++] = (byte)(accumulator >> bits);
			}
		}

		int remainder = symbols % 4;
		if (remainder == 1)
		{
			throw new CodecException(
				$"The last group has a single character at {Place(lastSymbol)}. Base64 ends in groups of two, three or four, so one is missing or extra.",
				lastSymbol);
		}

		int expectedPadding = (4 - remainder) % 4;
		if (padding > 0 && padding != expectedPadding)
		{
			throw new CodecException(expectedPadding == 0
				? "There is padding (=) after a complete group, where none belongs."
				: string.Create(CultureInfo.InvariantCulture, $"The padding does not fit: the last group needs {expectedPadding} '=' but has {padding}."));
		}

		string? note = (accumulator & ((1 << bits) - 1)) != 0
			? "The last character carries bits past the end of the data. Decoders drop them, so another text decodes to the same bytes."
			: null;
		return (output[..written], note);
	}

	private static int ValueOf(char c, bool urlSafe) => c switch
	{
		>= 'A' and <= 'Z' => c - 'A',
		>= 'a' and <= 'z' => c - 'a' + 26,
		>= '0' and <= '9' => c - '0' + 52,
		'+' when !urlSafe => 62,
		'/' when !urlSafe => 63,
		'-' when urlSafe => 62,
		'_' when urlSafe => 63,
		_ => -1,
	};

	private static string NotBase64(char c, int index, bool urlSafe)
	{
		string message = $"{Describe(c)} at {Place(index)} is not a {(urlSafe ? "Base64url" : "Base64")} character.";
		return (urlSafe, c) switch
		{
			(false, '-' or '_') => message + " It looks like Base64url.",
			(true, '+' or '/') => message + " It looks like standard Base64.",
			_ => message,
		};
	}

	internal static string Describe(char c) => char.IsControl(c) || char.IsSurrogate(c)
		? string.Create(CultureInfo.InvariantCulture, $"Character U+{(int)c:X4}")
		: $"'{c}'";

	internal static string Place(int index) => string.Create(CultureInfo.InvariantCulture, $"character {index + 1}");
}
