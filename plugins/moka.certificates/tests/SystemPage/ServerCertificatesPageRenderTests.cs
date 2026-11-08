using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Moka.Certificates.Crypto;
using Moka.Certificates.ServerFiles;
using Moka.Certificates.SystemPage;
using Moka.Certificates.Tests.ServerFiles;
using Moka.Certificates.Tests.Tool;
using Moka.Red.ContextMenu;
using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.UI.Common.Editing;
using Mokaterm.UI.Common.ToolPages;

namespace Moka.Certificates.Tests.SystemPage;

/// <summary>The System page over a view that answers its two scripts with fixture output, and one file's panel.</summary>
public sealed class ServerCertificatesPageRenderTests
{
	[Fact]
	public async Task Page_ListsWhatTheScriptsFoundWithTheCountsAbove()
	{
		using TestChain chain = TestMaterial.Chain();
		string files = $"""
			@@mc:file 0 pem
			{TestMaterial.Pem(chain.Leaf, chain.Intermediate)}
			@@mc:file 2 unreadable
			""";
		ScriptedContext context = new(Fixtures.Discovery, files);

		string html = await Rendering.RenderAsync<PageHost>(new() { [nameof(PageHost.Context)] = context }, configure: services =>
			services.AddSingleton<IMokaContextMenuService, MokaContextMenuService>());

		Assert.Equal(2, context.Reads);
		Assert.Contains("1 read, 0 expiring, 0 expired", html, StringComparison.Ordinal);
		Assert.Contains("moka-stat--tile", html, StringComparison.Ordinal);
		Assert.Contains("Not read", html, StringComparison.Ordinal);
		Assert.Contains("moka-table", html, StringComparison.Ordinal);
		Assert.Contains("www.example.com", html, StringComparison.Ordinal);
		Assert.Contains("/etc/letsencrypt/live/example.com/fullchain.pem", html, StringComparison.Ordinal);
		Assert.Contains("no access", html, StringComparison.Ordinal);
		Assert.Contains("mc-col-wide", html, StringComparison.Ordinal);
		Assert.Contains("Test renewal", html, StringComparison.Ordinal);
		Assert.DoesNotContain("No certificates found", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task FileDetails_ShowTheUsesCertbotTheChainAndTheServersCertificate()
	{
		using TestChain chain = TestMaterial.Chain();
		List<CertificateInfo> certificates = [CertificateReader.Describe(chain.Leaf, TestMaterial.Now, 30), CertificateReader.Describe(chain.Intermediate, TestMaterial.Now, 30)];
		ServerCertificateFile file = new()
		{
			Path = "/etc/letsencrypt/live/example.com/fullchain.pem",
			Status = ServerFileStatus.Read,
			Uses = [new CertificateUse("certbot", "example.com: example.com www.example.com"), new CertificateUse("nginx", "/etc/nginx/sites-enabled/shop")],
			Lineage = new CertbotCertificate { Name = "example.com", Expires = TestMaterial.Now.AddDays(90), State = "VALID: 89 days", KeyType = "ECDSA", Domains = ["example.com", "www.example.com"] },
			Certificates = certificates,
			Chain = ChainCheck.Analyze([chain.Leaf, chain.Intermediate]),
		};

		string html = await Rendering.RenderAsync<ServerFileDetails>(new() { [nameof(ServerFileDetails.File)] = file });

		Assert.Contains("Used by", html, StringComparison.Ordinal);
		Assert.Contains("/etc/nginx/sites-enabled/shop", html, StringComparison.Ordinal);
		Assert.Contains("VALID: 89 days", html, StringComparison.Ordinal);
		Assert.Contains("In this file", html, StringComparison.Ordinal);
		Assert.Contains("moka-chain-check", html, StringComparison.Ordinal);
		Assert.Contains("Not included", html, StringComparison.Ordinal);
		Assert.Contains("Moka Test Intermediate", html, StringComparison.Ordinal);
		Assert.Contains("Days left", html, StringComparison.Ordinal);
	}

	/// <summary>The System view's cascade around the page, which a static render cannot pass as a plain parameter.</summary>
	public sealed class PageHost : ComponentBase
	{
		[Parameter, EditorRequired]
		public ISystemPageContext Context { get; set; } = default!;

		protected override void BuildRenderTree(RenderTreeBuilder builder)
		{
			builder.OpenComponent<CascadingValue<ISystemPageContext>>(0);
			builder.AddComponentParameter(1, nameof(CascadingValue<ISystemPageContext>.Value), Context);
			builder.AddComponentParameter(2, nameof(CascadingValue<ISystemPageContext>.ChildContent), (RenderFragment)(page =>
			{
				page.OpenComponent<ServerCertificatesPage>(0);
				page.CloseComponent();
			}));
			builder.CloseComponent();
		}
	}

	/// <summary>A view that answers the discovery script and then the file read with the outputs given, and runs no change.</summary>
	private sealed class ScriptedContext(string discovery, string files) : ISystemPageContext
	{
		public int Reads { get; private set; }

		public ServerFacts Server { get; } = new()
		{
			DisplayName = "Ubuntu 24.04",
			Commands = new HashSet<string>([ServerCertificateCommands.Certbot, ServerCertificateCommands.Nginx], StringComparer.Ordinal),
		};

		public bool RootModeOn => true;

		public bool CanReadAsRoot => true;

		public string? RootChangeBlocked => null;

		public bool IsBusy => false;

		public bool IsVisible => true;

		public Task EnableRootAsync() => Task.CompletedTask;

		public void Navigate(string pageId, string? hint = null)
		{
		}

		public string? TakeHint() => null;

		public Task<SystemScriptResult> ReadAsync(SystemScript script, CancellationToken cancellationToken)
		{
			Reads++;
			return Task.FromResult(new SystemScriptResult { ExitCode = 0, Output = Reads == 1 ? discovery : files });
		}

		public Task<SystemChangeResult?> RunChangeAsync(SystemChange change) => Task.FromResult<SystemChangeResult?>(null);

		public Task<RemoteTextEditResult> EditFileAsync(string path, bool asRoot, bool createNew, CancellationToken cancellationToken) =>
			Task.FromResult(RemoteTextEditResult.Closed);
	}
}
