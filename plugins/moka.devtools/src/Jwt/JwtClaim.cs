using System.Text;
using System.Text.Json;

namespace Moka.DevTools.Jwt;

/// <summary>One top-level claim of the payload: its name and its value as formatted JSON, which the view masks.</summary>
public sealed record JwtClaim(string Name, string Json, JsonValueKind Kind)
{
	// A claim can be an email address or a role list; the generated ToString prints the name only.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Name = ").Append(Name).Append(", Kind = ").Append(Kind);
		return true;
	}
}
