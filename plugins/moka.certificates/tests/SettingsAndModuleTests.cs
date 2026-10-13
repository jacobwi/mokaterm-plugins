using Microsoft.Extensions.DependencyInjection;
using Moka.Certificates.Crypto;
using Moka.Certificates.ServerFiles;
using Moka.Certificates.Settings;
using Moka.Certificates.SystemPage;
using Moka.Certificates.Tool;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;

namespace Moka.Certificates.Tests;

public sealed class SettingsAndModuleTests
{
	[Fact]
	public void Clamped_HoldsEveryValueToItsRange()
	{
		CertificateSettings wild = new()
		{
			ValidityDays = 0,
			ExpiringDays = 9999,
			ServerCheckTimeoutSeconds = 1,
			KeyAlgorithm = (KeyAlgorithm)42,
			RsaKeySize = (RsaKeySize)(-1),
			Curve = (EllipticCurve)9,
			SignatureHash = (SignatureHash)7,
			PfxEncryption = (PfxEncryption)3,
			ExtraPaths = "relative/path",
		};

		CertificateSettings clamped = wild.Clamped();

		Assert.Equal(CertificateSettings.MinValidityDays, clamped.ValidityDays);
		Assert.Equal(CertificateSettings.MaxExpiringDays, clamped.ExpiringDays);
		Assert.Equal(CertificateSettings.MinServerCheckSeconds, clamped.ServerCheckTimeoutSeconds);
		Assert.Equal(KeyAlgorithm.Ecdsa, clamped.KeyAlgorithm);
		Assert.Equal(RsaKeySize.Bits3072, clamped.RsaKeySize);
		Assert.Equal(EllipticCurve.P256, clamped.Curve);
		Assert.Equal(SignatureHash.Sha256, clamped.SignatureHash);
		Assert.Equal(PfxEncryption.Aes256, clamped.PfxEncryption);
		Assert.Equal("", clamped.ExtraPaths);
		Assert.Equal(new CertificateSettings(), new CertificateSettings().Clamped());
		Assert.Equal(CertificateSettings.MaxValidityDays, (new CertificateSettings { ValidityDays = int.MaxValue }).Clamped().ValidityDays);
	}

	[Fact]
	public void ExtraPaths_AreSplitTrimmedAndChecked()
	{
		CertificateSettings settings = new CertificateSettings { ExtraPaths = " /etc/ssl/a.pem ,/opt/certs,\n/etc/ssl/a.pem " }.Clamped();

		Assert.Equal("/etc/ssl/a.pem, /opt/certs", settings.ExtraPaths);
		Assert.Equal(["/etc/ssl/a.pem", "/opt/certs"], settings.ExtraPathList());
		Assert.Null(CertificateSettings.ExtraPathsProblem(""));
		Assert.Contains("absolute path", CertificateSettings.ExtraPathsProblem("/ok, not/ok"), StringComparison.Ordinal);
		Assert.Contains("At most 20", CertificateSettings.ExtraPathsProblem(string.Join(",", Enumerable.Range(0, 21).Select(index => $"/p{index}"))), StringComparison.Ordinal);
	}

	[Fact]
	public void SectionKey_IsUnderThePluginsOwnName() => Assert.Equal("plugin.moka.certificates", CertificateSettings.SectionKey);

	[Fact]
	public void Module_RegistersTheToolThePageAndTheSettings()
	{
		ServiceCollection services = new();

		new CertificatesModule().ConfigureServices(services);
		using ServiceProvider provider = services.BuildServiceProvider();

		AppToolDescriptor tool = Assert.Single(provider.GetServices<AppToolDescriptor>());
		Assert.Equal("certificates", tool.Id);
		Assert.Equal(typeof(CertificatesTool), tool.Component);
		Assert.Equal(SessionToolSize.Wide, tool.Size);

		SystemPageDescriptor page = Assert.Single(provider.GetServices<SystemPageDescriptor>());
		Assert.Equal("server-certificates", page.Id);
		Assert.Equal(typeof(ServerCertificatesPage), page.Component);
		Assert.Equal(ServerCertificateCommands.Probed, page.Commands);

		SettingsPageDescriptor settings = Assert.Single(provider.GetServices<SettingsPageDescriptor>());
		Assert.Equal(CertificateSettings.SectionKey, settings.Id);
		Assert.Equal(SettingsPageGroup.Plugins, settings.Group);
		Assert.Equal(typeof(CertificateSettingsPage), settings.Component);
		Assert.Equal(CertificateSettings.SectionKey, Assert.Single(provider.GetServices<SettingsSectionType>()).Key);
		Assert.Equal("moka.certificates", new CertificatesModule().Info.Id);
	}
}
