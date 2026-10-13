using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Moka.Certificates.Crypto;

/// <summary>
/// Finds the PEM blocks in a text, whatever sits around them (OpenSSL's "Bag Attributes", comments, a log line), and
/// decodes one. The reader and the PEM to DER conversion both go through here.
/// </summary>
internal static class PemBlocks
{
	private const string BeginMarker = "-----BEGIN ";
	private const string DashMarker = "-----";

	public static bool HasBlocks(string text) => text.Contains(BeginMarker, StringComparison.Ordinal);

	/// <summary>Every block in order. A block without its END line ends the list with a problem.</summary>
	public static List<PemBlock> Find(string text, int max)
	{
		ArgumentNullException.ThrowIfNull(text);
		List<PemBlock> blocks = [];
		int position = 0;
		while (position < text.Length && blocks.Count < max)
		{
			int begin = text.IndexOf(BeginMarker, position, StringComparison.Ordinal);
			if (begin < 0)
			{
				break;
			}

			int labelStart = begin + BeginMarker.Length;
			int labelEnd = text.IndexOf(DashMarker, labelStart, StringComparison.Ordinal);
			int lineEnd = text.IndexOf('\n', labelStart);
			if (labelEnd < 0 || (lineEnd >= 0 && labelEnd > lineEnd))
			{
				blocks.Add(new PemBlock("", begin, 0, "A PEM block's BEGIN line is cut off."));
				break;
			}

			string label = text[labelStart..labelEnd];
			string endLine = "-----END " + label + DashMarker;
			int end = text.IndexOf(endLine, labelEnd, StringComparison.Ordinal);
			if (end < 0)
			{
				blocks.Add(new PemBlock(label, begin, 0, $"The {Shown(label)} block has no END line."));
				break;
			}

			position = end + endLine.Length;
			blocks.Add(new PemBlock(label, begin, position - begin, null));
		}

		return blocks;
	}

	/// <summary>The block's bytes. The caller clears them when they may be a key.</summary>
	public static bool TryDecode(string text, PemBlock block, [NotNullWhen(true)] out byte[]? der, [NotNullWhen(false)] out string? problem)
	{
		ArgumentNullException.ThrowIfNull(text);
		der = null;
		ReadOnlySpan<char> span = text.AsSpan(block.Start, block.Length);
		if (!PemEncoding.TryFind(span, out PemFields fields))
		{
			// RFC 7468 PEM has no headers; OpenSSL's old encrypted keys put Proc-Type and DEK-Info inside the block.
			problem = span.Contains("Proc-Type:", StringComparison.Ordinal)
				? $"The {Shown(block.Label)} block uses OpenSSL's old encryption (Proc-Type headers), which is not read here. Convert it first with: openssl pkcs8 -topk8 -in key.pem -out key-pkcs8.pem"
				: $"The {Shown(block.Label)} block is damaged: its base64 does not decode.";
			return false;
		}

		byte[] buffer = new byte[fields.DecodedDataLength];
		if (!Convert.TryFromBase64Chars(span[fields.Base64Data], buffer, out int written) || written != buffer.Length)
		{
			CryptographicOperations.ZeroMemory(buffer);
			problem = $"The {Shown(block.Label)} block is damaged: its base64 does not decode.";
			return false;
		}

		der = buffer;
		problem = null;
		return true;
	}

	/// <summary>A label as it may be shown back to the user: it came from their text, so it is kept short.</summary>
	public static string Shown(string label) => label.Length > 40 ? label[..40] + "..." : label;
}
