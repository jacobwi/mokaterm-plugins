using System.Text;

namespace Moka.DevTools.Codecs;

/// <summary>One run of a codec: what to do, its options, and the text typed or the bytes of a file that was opened.</summary>
public sealed record CodecRequest
{
	public CodecKind Kind { get; init; }

	public CodecDirection Direction { get; init; }

	public string Text { get; init; } = "";

	/// <summary>An opened file's bytes, which replace <see cref="Text"/>.</summary>
	public byte[]? FileBytes { get; init; }

	/// <summary>Base64 and Base64url: write the trailing '='.</summary>
	public bool Padding { get; init; } = true;

	public UrlMode UrlMode { get; init; }

	/// <summary>URL decoding: read '+' as a space, the way HTML forms send it.</summary>
	public bool PlusIsSpace { get; init; }

	public bool UpperHex { get; init; }

	public HexSeparator Separator { get; init; }

	/// <summary>HTML encoding: write everything outside ASCII as a numeric reference.</summary>
	public bool EncodeNonAscii { get; init; }

	// What is being encoded may be a token or a key, so the generated ToString prints its size only.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Kind = ").Append(Kind).Append(", Direction = ").Append(Direction)
			.Append(", Text = ").Append(Text.Length).Append(" chars, FileBytes = ").Append(FileBytes?.Length ?? 0);
		return true;
	}
}
