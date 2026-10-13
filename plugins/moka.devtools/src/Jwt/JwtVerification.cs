namespace Moka.DevTools.Jwt;

/// <summary>Whether the signature held, in one sentence, and what is worth knowing about the key it was checked with.</summary>
public sealed record JwtVerification(JwtVerdict Verdict, string Message, IReadOnlyList<string> Notes)
{
	public static JwtVerification NotChecked(string message) => new(JwtVerdict.NotChecked, message, []);
}
