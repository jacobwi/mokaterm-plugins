using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Moka.Certificates.Tls;

namespace Moka.Certificates.Tests.Tls;

/// <summary>The judging half of the server check, fed handshakes recorded by hand rather than made over a network.</summary>
public sealed class TlsReportBuilderTests
{
	[Fact]
	public void Build_AGoodServer_IsTrustedWithNothingToSay()
	{
		using TestChain chain = TestMaterial.Chain();
		using TlsObservation observation = Observation(chain, "www.example.com", [chain.Leaf, chain.Intermediate], [chain.Leaf, chain.Intermediate, chain.Root]);

		TlsReport report = TlsReportBuilder.Build(observation, TestMaterial.Now, 30);

		Assert.True(report.Trusted);
		Assert.Empty(report.Problems);
		Assert.Empty(report.Warnings);
		Assert.Equal(HostNameResult.Matches, report.NameMatch);
		Assert.Equal("TLS 1.3", report.Protocol);
		Assert.Equal("TLS_AES_256_GCM_SHA384", report.Cipher);
		Assert.Equal("www.example.com:443 (192.0.2.10)", report.Endpoint);
		Assert.True(report.SentOrder!.InOrder);
		Assert.Equal(TestMaterial.Pem(chain.Leaf, chain.Intermediate), report.SentPem);
		Assert.Equal(3, report.Chain.Count);
	}

	[Fact]
	public void Build_WrongName_IsAProblemThatNamesWhatTheCertificateIsFor()
	{
		using TestChain chain = TestMaterial.Chain();
		using TlsObservation observation = Observation(chain, "shop.example.net", [chain.Leaf, chain.Intermediate], [chain.Leaf, chain.Intermediate, chain.Root]);

		TlsReport report = TlsReportBuilder.Build(observation, TestMaterial.Now, 30);

		Assert.False(report.Trusted);
		Assert.Equal("The certificate is not for shop.example.net. It is for www.example.com, example.com, 192.0.2.10.", Assert.Single(report.Problems));
	}

	[Fact]
	public void Build_MissingIntermediate_SaysWhatToFix()
	{
		using TestChain chain = TestMaterial.Chain();
		using TlsObservation observation = Observation(chain, "www.example.com", [chain.Leaf], [chain.Leaf], X509ChainStatusFlags.PartialChain);

		TlsReport report = TlsReportBuilder.Build(observation, TestMaterial.Now, 30);

		Assert.False(report.Trusted);
		Assert.Contains("an intermediate certificate is missing", report.Problems[0], StringComparison.Ordinal);
		Assert.Contains("fullchain.pem", report.Problems[1], StringComparison.Ordinal);
	}

	[Fact]
	public void Build_OutOfOrderChainAndASentRoot_AreWarnings()
	{
		using TestChain chain = TestMaterial.Chain();
		using TlsObservation observation = Observation(chain, "www.example.com", [chain.Leaf, chain.Root, chain.Intermediate], [chain.Leaf, chain.Intermediate, chain.Root]);

		TlsReport report = TlsReportBuilder.Build(observation, TestMaterial.Now, 30);

		Assert.True(report.Trusted);
		Assert.Equal(2, report.Warnings.Count);
		Assert.StartsWith("The server sends its certificates out of order.", report.Warnings[0], StringComparison.Ordinal);
		Assert.Contains("also sends the root", report.Warnings[1], StringComparison.Ordinal);
	}

	[Fact]
	public void Build_ExpiredAndExpiringCertificates()
	{
		using TestChain chain = TestMaterial.Chain();
		using TlsObservation expired = Observation(chain, "www.example.com", [chain.Leaf, chain.Intermediate], [chain.Leaf, chain.Intermediate, chain.Root], X509ChainStatusFlags.NotTimeValid);
		using TlsObservation expiring = Observation(chain, "www.example.com", [chain.Leaf, chain.Intermediate], [chain.Leaf, chain.Intermediate, chain.Root]);

		TlsReport late = TlsReportBuilder.Build(expired, TestMaterial.Now.AddDays(100), 30);
		TlsReport soon = TlsReportBuilder.Build(expiring, TestMaterial.Now.AddDays(80), 30);

		Assert.StartsWith("The certificate expired on ", Assert.Single(late.Problems), StringComparison.Ordinal);
		Assert.True(soon.Trusted);
		Assert.StartsWith("The certificate expires in 9 days", Assert.Single(soon.Warnings), StringComparison.Ordinal);
	}

	[Fact]
	public void Build_OldProtocolAndUntrustedRoot()
	{
		using TestChain chain = TestMaterial.Chain();
		using TlsObservation observation = Observation(
			chain,
			"www.example.com",
			[chain.Leaf, chain.Intermediate],
			[chain.Leaf, chain.Intermediate, chain.Root],
			X509ChainStatusFlags.UntrustedRoot | X509ChainStatusFlags.RevocationStatusUnknown,
			(SslProtocols)192);

		TlsReport report = TlsReportBuilder.Build(observation, TestMaterial.Now, 30);

		Assert.False(report.Trusted);
		Assert.Equal("The chain ends at a root this machine does not trust.", Assert.Single(report.Problems));
		Assert.Contains("TLS 1.0", Assert.Single(report.Warnings), StringComparison.Ordinal);
	}

	[Fact]
	public void ChainProblems_DescribeEveryFlagOnceAndIgnoreRevocation()
	{
		IReadOnlyList<string> sentences = ChainProblems.Describe(
			X509ChainStatusFlags.NotSignatureValid | X509ChainStatusFlags.HasNotPermittedNameConstraint | X509ChainStatusFlags.InvalidNameConstraints | X509ChainStatusFlags.OfflineRevocation);

		Assert.Equal(["A signature in the chain does not verify.", "A name breaks the name constraints of a CA above it."], sentences);
		Assert.True(ChainProblems.IsClean(X509ChainStatusFlags.RevocationStatusUnknown));
		Assert.False(ChainProblems.IsClean(X509ChainStatusFlags.PartialChain));
	}

	[Fact]
	public void Build_WithoutVersionProbes_SaysNothingAboutVersions()
	{
		using TestChain chain = TestMaterial.Chain();
		using TlsObservation observation = Observation(chain, "www.example.com", [chain.Leaf, chain.Intermediate], [chain.Leaf, chain.Intermediate, chain.Root]);

		TlsReport report = TlsReportBuilder.Build(observation, TestMaterial.Now, 30);

		Assert.Empty(report.Versions);
		Assert.Null(report.VersionSummary);
		Assert.Equal(StartTlsMode.Plain, report.StartTls);
	}

	[Fact]
	public void Build_ModernServer_TakesTwelveAndNewerAndSaysSo()
	{
		using TestChain chain = TestMaterial.Chain();
		using TlsObservation observation = Observation(chain, "www.example.com", [chain.Leaf, chain.Intermediate], [chain.Leaf, chain.Intermediate, chain.Root]);

		TlsReport report = TlsReportBuilder.Build(observation, TestMaterial.Now, 30, Versions(((SslProtocols)192, TlsVersionOutcome.Refused), ((SslProtocols)768, TlsVersionOutcome.Refused), (SslProtocols.Tls12, TlsVersionOutcome.Accepted), (SslProtocols.Tls13, TlsVersionOutcome.Accepted)));

		Assert.Equal("The server takes TLS 1.2 and TLS 1.3 and refused TLS 1.0 and TLS 1.1.", report.VersionSummary);
		Assert.Empty(report.Warnings);
	}

	[Fact]
	public void Build_ServerThatStillTakesOldVersions_IsWarnedAbout()
	{
		using TestChain chain = TestMaterial.Chain();
		using TlsObservation observation = Observation(chain, "www.example.com", [chain.Leaf, chain.Intermediate], [chain.Leaf, chain.Intermediate, chain.Root]);

		TlsReport report = TlsReportBuilder.Build(observation, TestMaterial.Now, 30, Versions(((SslProtocols)192, TlsVersionOutcome.Accepted), ((SslProtocols)768, TlsVersionOutcome.Accepted), (SslProtocols.Tls12, TlsVersionOutcome.Accepted), (SslProtocols.Tls13, TlsVersionOutcome.Refused)));

		Assert.Equal("The server still accepts TLS 1.0 and TLS 1.1, which browsers no longer accept.", Assert.Single(report.Warnings));
		Assert.StartsWith("The server takes TLS 1.0, TLS 1.1 and TLS 1.2", report.VersionSummary, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_VersionsThisMachineWouldNotOffer_AreSaidHonestly()
	{
		using TestChain chain = TestMaterial.Chain();
		using TlsObservation observation = Observation(chain, "www.example.com", [chain.Leaf, chain.Intermediate], [chain.Leaf, chain.Intermediate, chain.Root]);

		TlsReport report = TlsReportBuilder.Build(observation, TestMaterial.Now, 30, Versions(((SslProtocols)192, TlsVersionOutcome.NotOfferedHere), ((SslProtocols)768, TlsVersionOutcome.NotOfferedHere), (SslProtocols.Tls12, TlsVersionOutcome.Accepted), (SslProtocols.Tls13, TlsVersionOutcome.Accepted)));

		Assert.Equal(
			"This machine refused to offer TLS 1.0 and TLS 1.1 even when asked, so the check cannot say whether the server would take them.",
			Assert.Single(report.Warnings));
		Assert.EndsWith("TLS 1.0 and TLS 1.1 could not be checked from this machine.", report.VersionSummary, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_NothingAccepted_SaysThat()
	{
		using TestChain chain = TestMaterial.Chain();
		using TlsObservation observation = Observation(chain, "www.example.com", [chain.Leaf, chain.Intermediate], [chain.Leaf, chain.Intermediate, chain.Root]);

		TlsReport report = TlsReportBuilder.Build(observation, TestMaterial.Now, 30, Versions(((SslProtocols)192, TlsVersionOutcome.NotOfferedHere), ((SslProtocols)768, TlsVersionOutcome.Refused), (SslProtocols.Tls12, TlsVersionOutcome.Refused), (SslProtocols.Tls13, TlsVersionOutcome.Refused)));

		Assert.StartsWith("None of the TLS versions tried completed a handshake.", report.VersionSummary, StringComparison.Ordinal);
	}

	// The old versions come as numbers, as everywhere: their enum members are marked obsolete.
	private static IReadOnlyList<TlsVersionResult> Versions(params (SslProtocols Protocol, TlsVersionOutcome Outcome)[] results) =>
		[.. results.Select(result => new TlsVersionResult(result.Protocol, result.Outcome))];

	private static TlsObservation Observation(
		TestChain chain,
		string host,
		X509Certificate2[] sent,
		X509Certificate2[] built,
		X509ChainStatusFlags status = X509ChainStatusFlags.NoError,
		SslProtocols protocol = SslProtocols.Tls13) => new()
	{
		Target = new TlsTarget(host, 443, host),
		RemoteAddress = IPAddress.Parse("192.0.2.10"),
		Protocol = protocol,
		CipherSuite = TlsCipherSuite.TLS_AES_256_GCM_SHA384,
		SentCertificates = [.. sent.Select(Copy)],
		BuiltChain = [.. built.Select(Copy)],
		ChainStatus = status,
		PolicyErrors = SslPolicyErrors.None,
		Elapsed = TimeSpan.FromMilliseconds(42),
	};

	private static X509Certificate2 Copy(X509Certificate2 certificate) => X509CertificateLoader.LoadCertificate(certificate.RawData);
}
