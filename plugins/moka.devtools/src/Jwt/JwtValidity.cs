using Moka.DevTools.Text;

namespace Moka.DevTools.Jwt;

/// <summary>Whether a token's exp and nbf hold at a moment, and how far that moment is from the edge that decides it.</summary>
public sealed record JwtValidity(JwtValidityState State, TimeSpan Distance)
{
	public static JwtValidity Of(JwtToken token, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(token);
		DateTimeOffset? expires = token.Times.FirstOrDefault(time => time.Claim == "exp")?.When;
		DateTimeOffset? notBefore = token.Times.FirstOrDefault(time => time.Claim == "nbf")?.When;
		return (expires, notBefore) switch
		{
			({ } exp, _) when exp <= now => new(JwtValidityState.Expired, now - exp),
			(_, { } nbf) when nbf > now => new(JwtValidityState.NotYetValid, nbf - now),
			({ } exp, _) => new(JwtValidityState.ValidNow, exp - now),
			_ => new(JwtValidityState.NoExpiry, TimeSpan.Zero),
		};
	}

	/// <summary>"Valid now", "Expired", "Not yet valid", "No expiry".</summary>
	public string Text => State switch
	{
		JwtValidityState.ValidNow => "Valid now",
		JwtValidityState.Expired => "Expired",
		JwtValidityState.NotYetValid => "Not yet valid",
		_ => "No expiry",
	};

	/// <summary>"Expires in 3 hours", "Expired 2 days ago", "Valid in 5 minutes", "There is no exp claim".</summary>
	public string Detail => State switch
	{
		JwtValidityState.ValidNow => $"Expires in {RelativeTime.Span(Distance)}",
		JwtValidityState.Expired => $"Expired {RelativeTime.Span(Distance)} ago",
		JwtValidityState.NotYetValid => $"Valid in {RelativeTime.Span(Distance)}",
		_ => "There is no exp claim",
	};
}
