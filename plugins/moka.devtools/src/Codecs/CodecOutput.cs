using System.Text;

namespace Moka.DevTools.Codecs;

/// <summary>
/// What a codec made: text, or bytes that the view shows as text only when they are UTF-8 and as hex otherwise, and a
/// note when the input was read in a way worth knowing about.
/// </summary>
public sealed record CodecOutput(string? Text, byte[]? Bytes, string? Note = null)
{
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Text = ").Append(Text?.Length ?? 0).Append(" chars, Bytes = ").Append(Bytes?.Length ?? 0);
		return true;
	}
}
