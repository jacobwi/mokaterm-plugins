using Moka.Kubernetes.Server;

namespace Moka.Kubernetes.Tests.Server;

/// <summary>The version JSON, the honest words for a cluster that cannot be reached, and cluster-info's URLs.</summary>
public sealed class KubectlVersionTests
{
	[Fact]
	public void Parse_ReadsBothVersions()
	{
		KubectlVersion version = KubectlVersion.Parse(Fixtures.VersionWithServer);

		Assert.Equal("v1.31.1", version.Client);
		Assert.Equal("v1.30.4", version.Server);
		Assert.True(version.HasServer);
	}

	[Fact]
	public void Parse_WithoutAServerVersion_SaysSoByShape()
	{
		KubectlVersion version = KubectlVersion.Parse(Fixtures.VersionClientOnly);

		Assert.Equal("v1.28.9", version.Client);
		Assert.Null(version.Server);
		Assert.False(version.HasServer);
	}

	[Fact]
	public void Parse_SkipsTheDeprecationNoticeBeforeTheJson()
	{
		KubectlVersion version = KubectlVersion.Parse(Fixtures.VersionDeprecatedNotice);

		Assert.Equal("v1.28.9", version.Client);
		Assert.Equal("v1.29.2", version.Server);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("not json at all")]
	[InlineData("[]")]
	public void Parse_OfAnythingElse_ReadsAsANumberlessVersion(string json)
	{
		KubectlVersion version = KubectlVersion.Parse(json);

		Assert.Null(version.Client);
		Assert.Null(version.Server);
		Assert.False(version.HasServer);
	}
}

/// <summary>The words for a kubectl that cannot answer, each named for what it is.</summary>
public sealed class KubectlErrorsTests
{
	[Fact]
	public void Words_ANeverRun_CarriesTheHostsOwnWords()
	{
		ServerRun run = ServerRun.Absent("The session is not connected, so nothing can run on the server.");

		Assert.Equal("The session is not connected, so nothing can run on the server.", KubectlErrors.Words(run));
	}

	[Fact]
	public void Words_ATimeout_SaysTheToolStoppedWaiting()
	{
		ServerRun run = ServerRun.GaveUp("kubectl 'version'", TimeSpan.FromSeconds(30));

		Assert.Contains("stopped waiting", KubectlErrors.Words(run), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(Fixtures.NoKubeconfigError, "no cluster configuration")]
	[InlineData(Fixtures.ConnectionRefusedError, "was refused")]
	[InlineData(Fixtures.NoSuchHostError, "does not resolve")]
	[InlineData(Fixtures.UnauthorizedError, "refused the credentials")]
	[InlineData(Fixtures.ForbiddenError, "answered but said no")]
	[InlineData(Fixtures.ContextNotFoundError, "names a context that is not in it")]
	[InlineData(Fixtures.CertificateError, "could not establish TLS")]
	[InlineData(Fixtures.NotFoundError, "no longer has what was asked for")]
	public void Words_TheKnownFailures_AreNamedForWhatTheyAre(string kubectlSaid, string words)
	{
		ServerRun run = new() { Command = "kubectl 'version'", ExitCode = 1, Error = kubectlSaid };

		Assert.Contains(words, KubectlErrors.Words(run), StringComparison.Ordinal);
	}

	[Fact]
	public void Words_APlainFailure_KeepsKubectlsOwnFirstLine()
	{
		ServerRun run = new() { Command = "kubectl 'version'", ExitCode = 1, Error = "error: something this tool never saw\nand more" };

		Assert.Equal("error: something this tool never saw", KubectlErrors.Words(run));
	}

	[Fact]
	public void Words_AnEmptyFailure_NamesTheStatus()
	{
		ServerRun run = new() { Command = "kubectl 'version'", ExitCode = 1 };

		Assert.Equal("kubectl ended with status 1.", KubectlErrors.Words(run));
	}
}

/// <summary>The URLs kubectl cluster-info names.</summary>
public sealed class ClusterInfoTextTests
{
	[Fact]
	public void Parse_ReadsTheControlPlaneAndTheServices()
	{
		IReadOnlyList<ClusterService> services = ClusterInfoText.Parse(Fixtures.ClusterInfo);

		Assert.Equal(2, services.Count);
		Assert.Equal("Kubernetes control plane", services[0].Name);
		Assert.Equal("https://192.0.2.10:6443", services[0].Url);
		Assert.Equal("CoreDNS", services[1].Name);
		Assert.Contains("/kube-dns:dns/proxy", services[1].Url, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_LeavesTheDebuggingAdviceAlone()
	{
		foreach (ClusterService service in ClusterInfoText.Parse(Fixtures.ClusterInfo))
		{
			Assert.DoesNotContain("debug", service.Name, StringComparison.OrdinalIgnoreCase);
		}
	}

	[Theory]
	[InlineData("")]
	[InlineData("To further debug and diagnose cluster problems, use 'kubectl cluster-info dump'.")]
	[InlineData("Something else entirely")]
	[InlineData("Nothing is running at ftp://old.example")]
	public void Parse_OfLinesThatAreNotServices_ReadsNone(string text)
	{
		Assert.Empty(ClusterInfoText.Parse(text));
	}
}
