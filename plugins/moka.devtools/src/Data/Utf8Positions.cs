using System.Text;
using Moka.DevTools.Text;

namespace Moka.DevTools.Data;

/// <summary>
/// Turns the byte places System.Text.Json reports into the line and column a person counts, since a line with accents
/// or emoji has more bytes than characters before the error.
/// </summary>
internal static class Utf8Positions
{
	/// <summary>The place of byte <paramref name="offset"/> in the whole document.</summary>
	public static TextPosition OfOffset(ReadOnlySpan<byte> utf8, long offset)
	{
		int end = (int)Math.Clamp(offset, 0, utf8.Length);
		int line = 1;
		int lineStart = 0;
		for (int index = 0; index < end; index++)
		{
			if (utf8[index] == (byte)'\n')
			{
				line++;
				lineStart = index + 1;
			}
		}

		return new TextPosition(line, CountCharacters(utf8[lineStart..end]) + 1);
	}

	/// <summary>The place of byte <paramref name="bytePosition"/> within line <paramref name="lineNumber"/>, both from 0.</summary>
	public static TextPosition OfLine(ReadOnlySpan<byte> utf8, long lineNumber, long bytePosition)
	{
		int lineStart = 0;
		long line = 0;
		for (int index = 0; index < utf8.Length && line < lineNumber; index++)
		{
			if (utf8[index] == (byte)'\n')
			{
				line++;
				lineStart = index + 1;
			}
		}

		int end = (int)Math.Clamp(lineStart + bytePosition, lineStart, utf8.Length);
		return new TextPosition((int)lineNumber + 1, CountCharacters(utf8[lineStart..end]) + 1);
	}

	// Characters as people count them: a UTF-8 lead byte starts one, continuation bytes do not.
	private static int CountCharacters(ReadOnlySpan<byte> bytes)
	{
		int count = 0;
		foreach (byte value in bytes)
		{
			if ((value & 0xC0) != 0x80 && value != (byte)'\r')
			{
				count++;
			}
		}

		return count;
	}

	public static byte[] Encode(string text)
	{
		try
		{
			return StrictUtf8.Encode(text);
		}
		catch (FormatException ex)
		{
			throw new DataException(ex.Message);
		}
		catch (EncoderFallbackException ex)
		{
			throw new DataException("The text holds a character UTF-8 cannot carry.", ex);
		}
	}
}
