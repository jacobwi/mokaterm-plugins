using System.Text;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Components;

/// <summary>What an <see cref="InputBox"/> holds: typed or pasted text, or a binary file that was opened.</summary>
public sealed record InputContent
{
	public static InputContent Empty { get; } = new();

	public string Text { get; init; } = "";

	/// <summary>A binary file's bytes; null while the box holds text.</summary>
	public byte[]? Bytes { get; init; }

	public string? FileName { get; init; }

	public bool IsEmpty => Bytes is null && string.IsNullOrWhiteSpace(Text);

	/// <summary>For the certificate reader.</summary>
	public CryptoInput ToCryptoInput() => Bytes is { } bytes ? CryptoInput.FromFile(FileName ?? "file", bytes) : CryptoInput.FromText(Text);

	/// <summary>The data itself: a file's bytes, or the text as UTF-8.</summary>
	public byte[] ToData() => Bytes ?? Encoding.UTF8.GetBytes(Text);

	/// <summary>
	/// A binary structure given any way: a binary file, the first PEM block of a text, or base64 or hex. Null when the
	/// text is none of those.
	/// </summary>
	public byte[]? ToStructure()
	{
		if (Bytes is { } bytes)
		{
			return bytes;
		}

		if (PemBlocks.HasBlocks(Text))
		{
			foreach (PemBlock block in PemBlocks.Find(Text, 1))
			{
				if (block.Problem is null && PemBlocks.TryDecode(Text, block, out byte[]? der, out _))
				{
					return der;
				}
			}

			return null;
		}

		return BinaryText.TryParse(Text, out byte[]? decoded) ? decoded : null;
	}

	// A pasted key is a secret, so the generated ToString prints sizes only.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Text = ").Append(Text.Length).Append(" chars, Bytes = ").Append(Bytes?.Length ?? 0);
		return true;
	}
}
