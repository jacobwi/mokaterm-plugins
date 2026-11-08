using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Moka.Certificates.Tls;
using Moka.Certificates.Tool;

namespace Moka.Certificates.Tests.Tool;

/// <summary>The server check's result screen, drawn from handshakes recorded by hand: no check ever runs here.</summary>
public sealed class ServerReportRenderTests
{
	[Fact]
	public async Task Report_AGoodServer_JudgesEveryStepAndShowsTheChainAndTheServersCertificate()
	{
		using TestChain chain = TestMaterial.Chain();
		using TlsObservation observation = Observation("www.example.com", [chain.Leaf, chain.Intermediate], [chain.Leaf, chain.Intermediate, chain.Root]);
		TlsReport report = TlsReportBuilder.Build(observation, TestMaterial.Now, 30, [new(SslProtocols.Tls12, TlsVersionOutcome.Accepted), new(SslProtocols.Tls13, TlsVersionOutcome.Accepted)]);

		string html = await Rendering.RenderAsync<ServerReport>(new() { [nameof(ServerReport.Report)] = report });

		Assert.Contains("A client would accept it", html, StringComparison.Ordinal);
		foreach (string step in new[] { "Connect", "Handshake", "Name", "Dates", "Trust", "Order" })
		{
			Assert.Contains($">{step}", html, StringComparison.Ordinal);
		}

		Assert.Contains("Matches", html, StringComparison.Ordinal);
		Assert.Contains("Trusted", html, StringComparison.Ordinal);
		Assert.Contains("TLS 1.3", html, StringComparison.Ordinal);
		Assert.Contains("TLS_AES_256_GCM_SHA384", html, StringComparison.Ordinal);
		Assert.Contains("Takes it", html, StringComparison.Ordinal);
		Assert.Contains("Sent by the server", html, StringComparison.Ordinal);
		Assert.Contains("Chain on this machine", html, StringComparison.Ordinal);
		Assert.Contains("The certificate is for www.example.com, in date, and its chain is trusted on this machine.", html, StringComparison.Ordinal);
		Assert.Contains("Server, issued by Moka Test Intermediate", html, StringComparison.Ordinal);
		Assert.Contains("Share of its validity left", html, StringComparison.Ordinal);
		Assert.Contains("Chain as sent", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Report_WrongNameAndMissingIntermediate_FailTheirStepsAndSayWhy()
	{
		using TestChain chain = TestMaterial.Chain();
		using TlsObservation observation = Observation("shop.example.net", [chain.Leaf], [chain.Leaf], X509ChainStatusFlags.PartialChain);
		TlsReport report = TlsReportBuilder.Build(observation, TestMaterial.Now, 30);

		string html = await Rendering.RenderAsync<ServerReport>(new() { [nameof(ServerReport.Report)] = report });

		Assert.Contains("A client would refuse it", html, StringComparison.Ordinal);
		Assert.Contains("Wrong name", html, StringComparison.Ordinal);
		Assert.Contains("Not trusted", html, StringComparison.Ordinal);
		Assert.Contains("Not found", html, StringComparison.Ordinal);
		Assert.Contains("The certificate is not for shop.example.net.", html, StringComparison.Ordinal);
		Assert.Contains("fullchain.pem", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">Order", html, StringComparison.Ordinal);
	}

	private static TlsObservation Observation(string host, X509Certificate2[] sent, X509Certificate2[] built, X509ChainStatusFlags status = X509ChainStatusFlags.NoError) => new()
	{
		Target = new TlsTarget(host, 443, host),
		RemoteAddress = IPAddress.Parse("192.0.2.10"),
		Protocol = SslProtocols.Tls13,
		CipherSuite = TlsCipherSuite.TLS_AES_256_GCM_SHA384,
		SentCertificates = [.. sent.Select(Copy)],
		BuiltChain = [.. built.Select(Copy)],
		ChainStatus = status,
		PolicyErrors = SslPolicyErrors.None,
		Elapsed = TimeSpan.FromMilliseconds(42),
	};

	private static X509Certificate2 Copy(X509Certificate2 certificate) => X509CertificateLoader.LoadCertificate(certificate.RawData);
}
