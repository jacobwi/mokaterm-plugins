using System.Text;

namespace Moka.Certificates.Tool;

/// <summary>One output of a conversion: text to show, or bytes to save, or both.</summary>
/// <param name="Title">What it is, for the output's label.</param>
/// <param name="Text">PEM or base64 to show and copy, or null for bytes only.</param>
/// <param name="FileName">The name to save the text as.</param>
/// <param name="Bytes">A binary form to save.</param>
/// <param name="BytesFileName">The name to save the bytes as.</param>
/// <param name="Secret">It holds a private key: hidden until shown, copied as a secret.</param>
/// <param name="Detail">A note beside the title.</param>
public sealed record ConvertedOutput(string Title, string? Text, string? FileName, byte[]? Bytes, string? BytesFileName, bool Secret, string? Detail = null)
{
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Title = ").Append(Title).Append(", Secret = ").Append(Secret);
		return true;
	}
}
