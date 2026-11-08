using Moka.DevTools.Components;
using Moka.DevTools.Jwt;
using Moka.DevTools.Tests.Shared;

namespace Moka.DevTools.Tests.Jwt;

public sealed class JwtClaimListTests
{
	[Fact]
	public async Task Claims_ShowTheirNamesAndMeaningsAndHideTheirValues()
	{
		JwtToken token = JwtDecoder.Decode(TestTokens.JwtIo);

		string html = await Render.HtmlAsync<JwtClaimList>(new()
		{
			[nameof(JwtClaimList.Claims)] = token.Claims,
			[nameof(JwtClaimList.PayloadJson)] = token.PayloadJson,
		});

		Assert.Contains(">sub<", html, StringComparison.Ordinal);
		Assert.Contains(">name<", html, StringComparison.Ordinal);
		Assert.Contains(">Subject<", html, StringComparison.Ordinal);
		Assert.Contains(">Issued at<", html, StringComparison.Ordinal);
		Assert.Equal(3, Render.Count(html, JwtClaimList.Mask));
		Assert.DoesNotContain("John Doe", html, StringComparison.Ordinal);
		Assert.DoesNotContain("1234567890", html, StringComparison.Ordinal);
		Assert.DoesNotContain("1516239022", html, StringComparison.Ordinal);
		Assert.Contains("3 claims, hidden until shown", html, StringComparison.Ordinal);
		Assert.Contains("Show all", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Show sub\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy the payload\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-label=\"Copy sub\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Payload_WithoutJson_OffersNoCopy()
	{
		JwtToken token = JwtDecoder.Decode(TestTokens.JwtIo);

		string html = await Render.HtmlAsync<JwtClaimList>(new() { [nameof(JwtClaimList.Claims)] = token.Claims });

		Assert.DoesNotContain("Copy the payload", html, StringComparison.Ordinal);
	}
}
