using System.Text;

namespace Moka.Certificates.Crypto;

/// <summary>
/// What the user pasted or opened. A file that holds text (PEM, or base64 without armor) is read as text; anything else
/// stays bytes, which is how DER and PFX files arrive.
/// </summary>
public sealed class CryptoInput
{
	// Notepad and friends start a UTF-8 file with one; it is not part of the PEM.
	private const char ByteOrderMark = (char)0xFEFF;

	private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	private CryptoInput(string? text, byte[]? bytes, string? fileName)
	{
		Text = text;
		Bytes = bytes;
		FileName = fileName;
	}

	/// <summary>Pasted text, or a file that turned out to be text.</summary>
	public string? Text { get; }

	/// <summary>A binary file, such as DER or PFX.</summary>
	public byte[]? Bytes { get; }

	/// <summary>The opened file's name, or null for pasted text.</summary>
	public string? FileName { get; }

	public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && (Bytes is null || Bytes.Length == 0);

	public static CryptoInput FromText(string? text) => new(text ?? "", null, null);

	/// <summary>A file's bytes: read as text when they are PEM or base64, kept as bytes otherwise.</summary>
	public static CryptoInput FromFile(string fileName, byte[] bytes)
	{
		ArgumentNullException.ThrowIfNull(bytes);
		return TryReadText(bytes) is { } text ? new CryptoInput(text, null, fileName) : new CryptoInput(null, bytes, fileName);
	}

	/// <summary>The bytes as text when they are PEM or base64; null for anything binary.</summary>
	internal static string? TryReadText(byte[] bytes)
	{
		if (bytes.Length == 0)
		{
			return null;
		}

		string text;
		try
		{
			text = StrictUtf8.GetString(bytes);
		}
		catch (DecoderFallbackException)
		{
			return null;
		}

		// DER is a valid UTF-8 string often enough when it is short, but it is never PEM or base64 throughout.
		if (text.Contains('\0', StringComparison.Ordinal))
		{
			return null;
		}

		if (text.TrimStart(ByteOrderMark).Contains("-----BEGIN ", StringComparison.Ordinal) || BinaryText.IsBase64(text))
		{
			return text.TrimStart(ByteOrderMark);
		}

		return null;
	}
}
