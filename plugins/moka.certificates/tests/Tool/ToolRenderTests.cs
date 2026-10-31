using Moka.Certificates.Settings;
using Moka.Certificates.Tool;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Plugin.TestHost;

namespace Moka.Certificates.Tests.Tool;

/// <summary>Every section and the settings page render without a failure on both hosts, before anything has been checked or made.</summary>
public sealed class ToolRenderTests
{
	[Theory]
	[InlineData(HostKind.Desktop)]
	[InlineData(HostKind.Web)]
	public async Task Tool_RendersEverySection(HostKind host)
	{
		string html = await StaticRender.RenderAsync<CertificatesTool>([], host);

		foreach (string title in new[] { "Inspect", "Generate", "Convert", "Sign and verify", "Encrypt", "Check a server" })
		{
			Assert.Contains(title, html, StringComparison.Ordinal);
		}

		Assert.Contains("mc-tool", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_OnTheWebHost_SaysTheServerCheckIsDesktopOnly()
	{
		string html = await StaticRender.RenderAsync<CertificatesTool>([], HostKind.Web);

		Assert.Contains("Only in the desktop app", html, StringComparison.Ordinal);
		Assert.Contains("Check a server: On the web host", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_OnTheDesktopHost_ShowsTheServerCheckForm()
	{
		string html = await StaticRender.RenderAsync<CertificatesTool>([], HostKind.Desktop);

		Assert.Contains("Check a server", html, StringComparison.Ordinal);
		Assert.Contains("TLS starts", html, StringComparison.Ordinal);
		Assert.Contains("SNI name", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(HostKind.Desktop)]
	[InlineData(HostKind.Web)]
	public async Task SettingsPage_RendersEveryRow(HostKind host)
	{
		string html = await StaticRender.RenderAsync<CertificateSettingsPage>([], host);

		foreach (string label in new[] { "Key algorithm", "RSA key size", "Curve", "Validity", "Signature hash", "PFX encryption", "Expiring after", "Time limit", "Refresh every", "Extra paths" })
		{
			Assert.Contains(label, html, StringComparison.Ordinal);
		}

		Assert.Contains("New keys and certificates", html, StringComparison.Ordinal);
		Assert.Contains("System page", html, StringComparison.Ordinal);
	}
}
