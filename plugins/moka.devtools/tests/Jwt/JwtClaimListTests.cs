using System.Net;
using Moka.DevTools.Components;
using Moka.DevTools.Jwt;
using Moka.DevTools.Tests.Rendering;

namespace Moka.DevTools.Tests.Jwt;

public sealed class JwtClaimListTests
{
	[Fact]
	public async Task Claims_ShowTheirNamesAndHideTheirValues()
	{
		JwtToken token = JwtDecoder.Decode(TestTokens.JwtIo);

		string html = WebUtility.HtmlDecode(await StaticRender.RenderAsync<JwtClaimList>(new()
		{
			[nameof(JwtClaimList.Claims)] = token.Claims,
			[nameof(JwtClaimList.PayloadJson)] = token.PayloadJson,
		}));

		Assert.Contains("\"sub\"", html, StringComparison.Ordinal);
		Assert.Contains("\"name\"", html, StringComparison.Ordinal);
		Assert.Contains(JwtClaimList.Mask, html, StringComparison.Ordinal);
		Assert.DoesNotContain("John Doe", html, StringComparison.Ordinal);
		Assert.DoesNotContain("1234567890", html, StringComparison.Ordinal);
		Assert.DoesNotContain("1516239022", html, StringComparison.Ordinal);
		Assert.Contains("Show all", html, StringComparison.Ordinal);
	}
}
