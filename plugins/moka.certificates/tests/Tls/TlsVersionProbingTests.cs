using System.Net.Sockets;
using System.Security.Authentication;
using Moka.Certificates.Tls;

namespace Moka.Certificates.Tests.Tls;

/// <summary>The parts of the pinned handshakes that need no network: which versions are tried and how a failure reads.</summary>
public sealed class TlsVersionProbingTests
{
	[Fact]
	public void Probed_AreTheFourVersionsOldestFirst()
	{
		Assert.Equal([(SslProtocols)192, (SslProtocols)768, SslProtocols.Tls12, SslProtocols.Tls13], TlsVersions.Probed);
		Assert.Equal("TLS 1.0", TlsReportBuilder.ProtocolName(TlsVersions.Probed[0]));
		Assert.Equal("TLS 1.1", TlsReportBuilder.ProtocolName(TlsVersions.Probed[1]));
	}

	[Fact]
	public void Classify_AFailingHandshakeThatWasOffered_IsTheServerRefusing()
	{
		Assert.Equal(TlsVersionOutcome.Refused, TlsProbe.Classify(new AuthenticationException(), offered: true));
		Assert.Equal(TlsVersionOutcome.Refused, TlsProbe.Classify(new IOException(), offered: true));
		Assert.Equal(TlsVersionOutcome.Refused, TlsProbe.Classify(new SocketException(), offered: true));
	}

	[Fact]
	public void Classify_AHandshakeThatNeverWentOut_IsThisMachineRefusingToOffer()
	{
		Assert.Equal(TlsVersionOutcome.NotOfferedHere, TlsProbe.Classify(new AuthenticationException(), offered: false));
	}

	[Fact]
	public void Classify_AConnectionFailureOnAnOfferedHandshake_IsStillARefusal()
	{
		Assert.Equal(TlsVersionOutcome.Refused, TlsProbe.Classify(new IOException(), offered: false));
		Assert.Equal(TlsVersionOutcome.Refused, TlsProbe.Classify(new SocketException(), offered: false));
		Assert.Equal(TlsVersionOutcome.Refused, TlsProbe.Classify(new OperationCanceledException(), offered: false));
	}

	[Fact]
	public void ProtocolName_NamesEveryVersionItProbes()
	{
		foreach (SslProtocols version in TlsVersions.Probed)
		{
			Assert.StartsWith("TLS 1.", TlsReportBuilder.ProtocolName(version), StringComparison.Ordinal);
		}
	}
}
