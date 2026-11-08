using System.Text;
using Moka.DevTools.Components;
using Moka.DevTools.Jwt;
using Moka.DevTools.Tests.Shared;

namespace Moka.DevTools.Tests.Jwt;

public sealed class JwtTokenViewTests
{
	private static readonly byte[] Key = Encoding.UTF8.GetBytes("a secret of thirty-two bytes, ok");

	[Fact]
	public async Task Token_ShowsItsFiguresAndKeepsItsClaimsHidden()
	{
		JwtToken token = JwtDecoder.Decode(TestTokens.Hmac("HS256", Key));

		string html = await RenderAsync(token);

		Assert.Contains(">HS256<", html, StringComparison.Ordinal);
		Assert.Contains("Shared secret (HMAC)", html, StringComparison.Ordinal);
		Assert.Contains("Valid now", html, StringComparison.Ordinal);
		Assert.Contains("Expires in 74 years", html, StringComparison.Ordinal);
		Assert.Contains("Not checked", html, StringComparison.Ordinal);
		Assert.Contains("Values hidden until shown", html, StringComparison.Ordinal);
		Assert.Equal(4, Render.Count(html, JwtClaimList.Mask));
		Assert.DoesNotContain("alice", html, StringComparison.Ordinal);
		Assert.DoesNotContain("admin", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Token_ShowsItsTimesHeaderAndSignature()
	{
		JwtToken token = JwtDecoder.Decode(TestTokens.Hmac("HS256", Key));

		string html = await RenderAsync(token);

		Assert.Contains("2100-01-01T00:00:00Z", html, StringComparison.Ordinal);
		Assert.Contains("Expires, exp", html, StringComparison.Ordinal);
		Assert.Contains("2023-11-14T22:13:20Z", html, StringComparison.Ordinal);
		Assert.Contains("Issued 23 months ago", html, StringComparison.Ordinal);
		Assert.Contains("\"alg\": \"HS256\"", html, StringComparison.Ordinal);
		Assert.Contains(token.SignatureText, html, StringComparison.Ordinal);
		Assert.Contains("32 bytes", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy the signature as hex\"", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(JwtVerdict.Verified, "Verified", "It matches the key")]
	[InlineData(JwtVerdict.Failed, "Failed", "It does not match the key")]
	public async Task Signature_TileFollowsTheCheck(JwtVerdict verdict, string text, string detail)
	{
		JwtToken token = JwtDecoder.Decode(TestTokens.Hmac("HS256", Key));

		string html = await RenderAsync(token, new JwtVerification(verdict, "Checked.", []));

		Assert.Contains($">{text}<", html, StringComparison.Ordinal);
		Assert.Contains(detail, html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Unsigned_SaysSoAndCannotBeChecked()
	{
		string payload = """{"sub":"mallory","exp":1700000000}""";
		JwtToken token = JwtDecoder.Decode($"{TestTokens.Part("""{"alg":"none"}""")}.{TestTokens.Part(payload)}.");

		string html = await RenderAsync(token);

		Assert.Contains("This token is not signed", html, StringComparison.Ordinal);
		Assert.Contains(">Unsigned<", html, StringComparison.Ordinal);
		Assert.Contains(">Expired<", html, StringComparison.Ordinal);
		Assert.Contains("Expired 23 months ago", html, StringComparison.Ordinal);
		Assert.Contains("(empty)", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mallory", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Payload_ThatIsNotAnObject_StaysHiddenWhole()
	{
		JwtToken token = JwtDecoder.Decode($"{TestTokens.Part("""{"alg":"HS256"}""")}.{TestTokens.Part("just a secret note")}.{TestTokens.B64(new byte[32])}");

		string html = await RenderAsync(token);

		Assert.Contains("Hidden. Copy it, save it, or show it here.", html, StringComparison.Ordinal);
		Assert.Contains("Not JSON", html, StringComparison.Ordinal);
		Assert.Contains("No expiry", html, StringComparison.Ordinal);
		Assert.DoesNotContain("just a secret note", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(JwtToken token, JwtVerification? verification = null) =>
		Render.HtmlAsync<JwtTokenView>(new()
		{
			[nameof(JwtTokenView.Token)] = token,
			[nameof(JwtTokenView.Verification)] = verification,
		});
}
