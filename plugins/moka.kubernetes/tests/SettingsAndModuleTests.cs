using Moka.Kubernetes.Settings;
using Mokaterm.Abstractions.Settings;

namespace Moka.Kubernetes.Tests;

public sealed class SettingsAndModuleTests
{
	[Fact]
	public void Clamped_HoldsEveryValueToItsRange()
	{
		KubernetesSettings wild = new()
		{
			KubectlPath = "/opt/kubectl",
			Namespace = "production",
			CommandTimeoutSeconds = 999,
		};

		KubernetesSettings clamped = wild.Clamped();

		Assert.Equal("/opt/kubectl", clamped.KubectlPath);
		Assert.Equal("production", clamped.Namespace);
		Assert.Equal(KubernetesSettings.MaxCommandTimeoutSeconds, clamped.CommandTimeoutSeconds);
		Assert.Equal(KubernetesSettings.MinCommandTimeoutSeconds, (wild with { CommandTimeoutSeconds = 0 }).Clamped().CommandTimeoutSeconds);
		Assert.Equal(new KubernetesSettings(), new KubernetesSettings().Clamped());
	}

	[Fact]
	public void Clamped_TrimsAndEmptiesValuesTheValidatorsRefuse()
	{
		KubernetesSettings settings = new KubernetesSettings { KubectlPath = "  /opt/kubectl  ", Namespace = "  production  " }.Clamped();

		Assert.Equal("/opt/kubectl", settings.KubectlPath);
		Assert.Equal("production", settings.Namespace);

		Assert.Equal("", (settings with { KubectlPath = "-n" }).Clamped().KubectlPath);
		Assert.Equal("", (settings with { Namespace = "Production" }).Clamped().Namespace);
		Assert.Equal("", (settings with { Namespace = "two words" }).Clamped().Namespace);
		Assert.Equal("", (settings with { Namespace = "-start" }).Clamped().Namespace);
		Assert.Equal("", (settings with { Namespace = new string('a', 64) }).Clamped().Namespace);
	}

	[Fact]
	public void Clamped_KeepsTheEmptyChoicesThatMeanKubectlsOwn()
	{
		KubernetesSettings settings = new KubernetesSettings { KubectlPath = "  ", Namespace = "" }.Clamped();

		Assert.Equal("", settings.KubectlPath);
		Assert.Equal("", settings.Namespace);
		Assert.Equal(TimeSpan.FromSeconds(30), settings.CommandTimeout);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("kubectl")]
	[InlineData("/usr/local/bin/kubectl")]
	[InlineData("C:/Program Files/kubectl.exe")]
	public void KubectlPathProblem_AllowsTheOrdinaryPaths(string? path)
	{
		Assert.Null(KubernetesSettings.KubectlPathProblem(path));
	}

	[Theory]
	[InlineData("-n")]
	[InlineData("--namespace")]
	public void KubectlPathProblem_RefusesADashTheShellWouldReadAsAFlag(string path)
	{
		Assert.NotNull(KubernetesSettings.KubectlPathProblem(path));
	}

	[Fact]
	public void KubectlPathProblem_RefusesLineBreaks()
	{
		Assert.NotNull(KubernetesSettings.KubectlPathProblem("/opt/kubectl\nrm -rf /"));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("default")]
	[InlineData("kube-system")]
	[InlineData("team-a1")]
	[InlineData("a")]
	public void NamespaceProblem_AllowsTheDNSLabelsAKubernetesNamespaceIs(string? value)
	{
		Assert.Null(KubernetesSettings.NamespaceProblem(value));
	}

	[Theory]
	[InlineData("Production")]
	[InlineData("two words")]
	[InlineData("under_score")]
	[InlineData("-start")]
	[InlineData("end-")]
	[InlineData("dot.in.the.middle")]
	public void NamespaceProblem_RefusesEverythingANamespaceCannotBe(string value)
	{
		Assert.NotNull(KubernetesSettings.NamespaceProblem(value));
	}

	[Fact]
	public void NamespaceProblem_RefusesALabelLongerThanDNSAllows()
	{
		Assert.NotNull(KubernetesSettings.NamespaceProblem(new string('a', KubernetesSettings.MaxNamespaceLength + 1)));
	}

	[Fact]
	public void SectionKey_IsUnderThePluginsOwnName() => Assert.Equal("plugin.moka.kubernetes", KubernetesSettings.SectionKey);
}
