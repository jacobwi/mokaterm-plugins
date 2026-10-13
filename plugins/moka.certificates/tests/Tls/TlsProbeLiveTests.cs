using Moka.Certificates.Tls;

namespace Moka.Certificates.Tests.Tls;

/// <summary>
/// One real handshake, skipped unless MOKA_CERTIFICATES_TLS_HOST names a server (<c>host</c> or <c>host:port</c>), the
/// way Mokaterm gates its live protocol tests: the rest of the suite never touches the network.
/// </summary>
public sealed class TlsProbeLiveTests
{
	[Fact]
	public async Task Probe_RecordsTheChainProtocolAndCipher()
	{
		string? host = Environment.GetEnvironmentVariable("MOKA_CERTIFICATES_TLS_HOST");
		Assert.SkipWhen(string.IsNullOrWhiteSpace(host), "Set MOKA_CERTIFICATES_TLS_HOST to check a real server.");
		Assert.True(TlsTarget.TryParse(host, "", "", out TlsTarget? target, out string? problem), problem);

		using TlsObservation observation = await TlsProbe.ProbeAsync(target, TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
		TlsReport report = TlsReportBuilder.Build(observation, DateTimeOffset.UtcNow, 30);

		Assert.NotEmpty(report.Sent);
		Assert.NotEmpty(report.Chain);
		Assert.StartsWith("TLS 1.", report.Protocol, StringComparison.Ordinal);
		Assert.NotEqual("unknown", report.Cipher);
		Assert.Equal(HostNameResult.Matches, report.NameMatch);
		TestContext.Current.SendDiagnosticMessage($"{report.Endpoint} {report.Protocol} {report.Cipher}, sent {report.Sent.Count}, chain {report.Chain.Count}, trusted {report.Trusted}");
	}
}
