using System.Text;

namespace Moka.DevTools.Components;

/// <summary>
/// What a <see cref="TextInput"/> holds: typed text, text from a file too long for the box (kept beside it, named by
/// the file), or a file that is not text at all.
/// </summary>
public sealed record TextValue
{
	public static TextValue Empty { get; } = new();

	public string Text { get; init; } = "";

	/// <summary>Set when the content came from a file and is held outside the box.</summary>
	public string? FileName { get; init; }

	/// <summary>A file that is not UTF-8 text, for the sections that work on bytes.</summary>
	public byte[]? Bytes { get; init; }

	public bool IsFile => FileName is not null;

	public bool IsEmpty => Bytes is null && Text.Length == 0;

	/// <summary>The size shown on a file's chip.</summary>
	public long Length => Bytes?.LongLength ?? Text.Length;

	// The box may hold a token or a key; the generated ToString prints sizes only.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Text = ").Append(Text.Length).Append(" chars, Bytes = ").Append(Bytes?.Length ?? 0)
			.Append(", IsFile = ").Append(IsFile);
		return true;
	}
}
