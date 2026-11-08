using Moka.DevTools.Jwt;

namespace Moka.DevTools.Tests.Jwt;

public sealed class JwtValidityTests
{
	private static readonly DateTimeOffset Now = new(2025, 11, 8, 12, 0, 0, TimeSpan.Zero);

	[Theory]
	[InlineData("""{"exp":1762606800}""", JwtValidityState.ValidNow, "Valid now", "Expires in 1 hour")]
	[InlineData("""{"exp":1762596000}""", JwtValidityState.Expired, "Expired", "Expired 2 hours ago")]
	[InlineData("""{"exp":1762606800,"nbf":1762604100}""", JwtValidityState.NotYetValid, "Not yet valid", "Valid in 15 minutes")]
	[InlineData("""{"sub":"alice"}""", JwtValidityState.NoExpiry, "No expiry", "There is no exp claim")]
	public void Validity_ReadsExpAndNbf(string payload, JwtValidityState state, string text, string detail)
	{
		JwtToken token = JwtDecoder.Decode($"{TestTokens.Part("""{"alg":"HS256"}""")}.{TestTokens.Part(payload)}.{TestTokens.B64(new byte[32])}");

		JwtValidity validity = JwtValidity.Of(token, Now);

		Assert.Equal(state, validity.State);
		Assert.Equal(text, validity.Text);
		Assert.Equal(detail, validity.Detail);
	}

	[Fact]
	public void Expired_WinsOverNotYetValid() =>
		Assert.Equal(
			JwtValidityState.Expired,
			JwtValidity.Of(JwtDecoder.Decode($"{TestTokens.Part("""{"alg":"HS256"}""")}.{TestTokens.Part("""{"exp":1,"nbf":4102444800}""")}.{TestTokens.B64(new byte[32])}"), Now).State);

	[Theory]
	[InlineData("exp", "Expires")]
	[InlineData("sub", "Subject")]
	[InlineData("scp", "Scopes")]
	[InlineData("preferred_username", "User name")]
	public void ClaimNames_DescribeRegisteredClaims(string name, string meaning) => Assert.Equal(meaning, JwtClaimNames.Describe(name));

	[Fact]
	public void ClaimNames_LeaveOthersAlone() => Assert.Null(JwtClaimNames.Describe("role"));
}
