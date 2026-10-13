using System.Text;

namespace Moka.Certificates.Tool;

/// <summary>Text another section hands to Inspect. The sequence number tells a new request from a render of the same one.</summary>
public sealed record InspectRequest(string Text, int Sequence)
{
	// The text can be a private key; a record's generated ToString would print it.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Sequence = ").Append(Sequence);
		return true;
	}
}
