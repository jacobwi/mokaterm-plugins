using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Moka.Certificates.Crypto;
using Moka.Certificates.Tls;

namespace Moka.Certificates.Tests.Tls;

public sealed class HostNameMatchTests
{
	[Theory]
	[InlineData("www.example.com", "www.example.com", true)]
	[InlineData("WWW.Example.com", "www.example.com", true)]
	[InlineData("*.example.com", "www.example.com", true)]
	[InlineData("*.example.com", "example.com", false)]
	[InlineData("*.example.com", "a.b.example.com", false)]
	[InlineData("*.com", "example.com", false)]
	[InlineData("www.example.com.", "www.example.com", true)]
	[InlineData("www.example.com", "www.example.org", false)]
	public void Matches_FollowsTheWildcardRules(string pattern, string host, bool expected) =>
		Assert.Equal(expected, HostNameMatch.Matches(pattern, host));

	[Fact]
	public void Check_UsesDnsNamesAddressesAndOnlyThenTheCommonName()
	{
		using ECDsa key = TestMaterial.EcKey();
		using X509Certificate2 named = TestMaterial.SelfSigned(key, "cn.example", "*.example.com", "192.0.2.9", "2001:db8::9");
		CertificateRequest plain = new(new X500DistinguishedName("CN=legacy.example"), key, HashAlgorithmName.SHA256);
		using X509Certificate2 cnOnly = plain.CreateSelfSigned(TestMaterial.Now.AddDays(-1), TestMaterial.Now.AddDays(1));
		CertificateInfo withNames = CertificateReader.Describe(named, TestMaterial.Now, 30);
		CertificateInfo legacy = CertificateReader.Describe(cnOnly, TestMaterial.Now, 30);

		Assert.Equal(HostNameResult.Matches, HostNameMatch.Check(withNames, "api.example.com"));
		Assert.Equal(HostNameResult.Matches, HostNameMatch.Check(withNames, "192.0.2.9"));
		Assert.Equal(HostNameResult.Matches, HostNameMatch.Check(withNames, "2001:0db8::9"));
		Assert.Equal(HostNameResult.NoMatch, HostNameMatch.Check(withNames, "cn.example"));
		Assert.Equal(HostNameResult.NoMatch, HostNameMatch.Check(withNames, "192.0.2.10"));
		Assert.Equal(HostNameResult.CommonNameOnly, HostNameMatch.Check(legacy, "legacy.example"));
		Assert.Equal(HostNameResult.NoMatch, HostNameMatch.Check(legacy, "other.example"));
	}
}
