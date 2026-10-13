using Moka.DevTools.Tests.Rendering;
using Moka.DevTools.Tool;
using Mokaterm.Abstractions.Platform;

namespace Moka.DevTools.Tests.Tool;

/// <summary>Every section renders without a failure on both hosts, before any script has run.</summary>
public sealed class ToolRenderTests
{
	[Theory]
	[InlineData(HostKind.Desktop)]
	[InlineData(HostKind.Web)]
	public async Task Tool_RendersEverySection(HostKind host)
	{
		string html = await StaticRender.RenderAsync<DevToolsTool>([], host);

		foreach (string title in new[] { "JWT", "Encode and decode", "Hashes", "IDs", "Time", "JSON and YAML", "Regex" })
		{
			Assert.Contains(title, html, StringComparison.Ordinal);
		}

		Assert.Contains("dt-tool", html, StringComparison.Ordinal);
	}
}
