using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Moka.DevTools.Text;

/// <summary>
/// UTF-8 that refuses rather than replaces: bytes that are not valid UTF-8 are shown as hex by the caller, never turned
/// into replacement characters that would look like the real text.
/// </summary>
public static class StrictUtf8
{
	private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	/// <summary>UTF-8 without a byte order mark, which throws on a lone surrogate instead of writing a replacement.</summary>
	public static UTF8Encoding Encoding => Strict;

	public static bool TryDecode(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out string? text)
	{
		try
		{
			text = Strict.GetString(bytes);
			return true;
		}
		catch (DecoderFallbackException)
		{
			text = null;
			return false;
		}
	}

	/// <summary>The text's UTF-8 bytes. A lone surrogate cannot be written as UTF-8, so it is reported with its place.</summary>
	public static byte[] Encode(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		for (int index = 0; index < text.Length; index++)
		{
			char c = text[index];
			if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
			{
				index++;
			}
			else if (char.IsSurrogate(c))
			{
				throw new FormatException($"The text holds half of a character pair at {TextPosition.Of(text, index)}, which UTF-8 cannot carry.");
			}
		}

		return Strict.GetBytes(text);
	}

	/// <summary>The text without a leading byte order mark, which editors add and parsers trip over.</summary>
	public static string WithoutBom(string text) => text.Length > 0 && text[0] == (char)0xFEFF ? text[1..] : text;
}
