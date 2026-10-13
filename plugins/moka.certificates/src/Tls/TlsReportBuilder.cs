using System.Globalization;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tls;

/// <summary>
/// Judges a handshake: whether the name matches, whether the chain holds and in what order the server sends it, and
/// how long the certificate has left, with every problem in words. Pure, so it is tested without a network.
/// </summary>
public static class TlsReportBuilder
{
	public static TlsReport Build(TlsObservation observation, DateTimeOffset now, int expiringDays)
	{
		ArgumentNullException.ThrowIfNull(observation);
		if (observation.SentCertificates.Count == 0)
		{
			throw new CertificateException("The server completed the handshake without sending a certificate.");
		}

		List<CertificateInfo> sent = [.. observation.SentCertificates.Select(certificate => CertificateReader.Describe(certificate, now, expiringDays))];
		List<CertificateInfo> chain = [.. observation.BuiltChain.Select(certificate => CertificateReader.Describe(certificate, now, expiringDays))];
		CertificateInfo leaf = sent[0];
		string name = observation.Target.NameToCheck;
		HostNameResult match = HostNameMatch.Check(leaf, name);
		List<string> problems = [];
		List<string> warnings = [];

		switch (match)
		{
			case HostNameResult.NoMatch:
				problems.Add($"The certificate is not for {name}. It is for {NamesOf(leaf)}.");
				break;
			case HostNameResult.CommonNameOnly:
				problems.Add($"Only the common name matches {name}. Browsers ignore the common name: the name has to be among the certificate's DNS names.");
				break;
		}

		switch (leaf.Expiry)
		{
			case ExpiryState.Expired:
				problems.Add($"The certificate expired on {CertificateReport.Date(leaf.NotAfter)}.");
				break;
			case ExpiryState.NotYetValid:
				problems.Add($"The certificate is not valid until {CertificateReport.Date(leaf.NotBefore)}. Check this machine's clock too.");
				break;
			case ExpiryState.Expiring:
				warnings.Add($"The certificate {CertificateReport.ExpiryText(leaf)}.");
				break;
		}

		// The leaf's own dates are already said above; the chain flag then only adds something for the other certificates.
		X509ChainStatusFlags status = observation.ChainStatus;
		if (leaf.Expiry is ExpiryState.Expired or ExpiryState.NotYetValid
			&& chain.Skip(1).All(certificate => certificate.Expiry is ExpiryState.Valid or ExpiryState.Expiring))
		{
			status &= ~X509ChainStatusFlags.NotTimeValid;
		}

		problems.AddRange(ChainProblems.Describe(status));
		if (observation.ChainStatus.HasFlag(X509ChainStatusFlags.PartialChain) && sent.Count == 1 && !leaf.IsSelfIssued)
		{
			problems.Add("The server sends only its own certificate. Add the intermediate certificate to its configuration (on nginx, use fullchain.pem).");
		}

		ChainCheck? order = null;
		if (sent.Count > 1)
		{
			order = ChainCheck.Analyze(observation.SentCertificates);
			if (!order.InOrder)
			{
				warnings.Add($"The server sends its certificates out of order. {order.Summary}");
			}

			if (sent.Skip(1).Any(certificate => certificate.IsSelfSigned == true))
			{
				warnings.Add("The server also sends the root certificate. Clients ignore it, so leaving it out saves bytes on every handshake.");
			}
		}

		if (leaf.SignatureAlgorithmOid is OidNames.RsaSha1 or OidNames.EcdsaSha1)
		{
			problems.Add("The certificate is signed with SHA-1, which browsers refuse.");
		}

		if (leaf.Key.Limitation is { } weak && leaf.Key.Algorithm == "RSA")
		{
			warnings.Add(weak);
		}

		// TLS 1.0 and 1.1 sort below 1.2; SSL 2 and 3 below those. None means the platform did not say.
		if (observation.Protocol != SslProtocols.None && observation.Protocol < SslProtocols.Tls12)
		{
			warnings.Add($"The server agreed to {ProtocolName(observation.Protocol)}, which browsers no longer accept.");
		}

		bool trusted = problems.Count == 0
			&& ChainProblems.IsClean(observation.ChainStatus)
			&& (observation.PolicyErrors & SslPolicyErrors.RemoteCertificateNotAvailable) == 0;
		return new TlsReport
		{
			Endpoint = observation.RemoteAddress is { } address ? $"{observation.Target.Endpoint} ({address})" : observation.Target.Endpoint,
			ServerName = observation.Target.ServerName,
			Protocol = ProtocolName(observation.Protocol),
			Cipher = observation.CipherSuite?.ToString() ?? "unknown",
			Leaf = leaf,
			Sent = sent,
			Chain = chain,
			NameChecked = name,
			NameMatch = match,
			Trusted = trusted,
			Problems = problems,
			Warnings = warnings,
			SentOrder = order,
			SentPem = CertificateConverter.CertificatesPem(observation.SentCertificates),
			ChainPem = CertificateConverter.CertificatesPem(observation.BuiltChain),
			Elapsed = observation.Elapsed,
		};
	}

	// The old versions are named by number: their enum members are obsolete, and the report still has to name them.
	public static string ProtocolName(SslProtocols protocol) => (int)protocol switch
	{
		12288 => "TLS 1.3",
		3072 => "TLS 1.2",
		768 => "TLS 1.1",
		192 => "TLS 1.0",
		48 => "SSL 3.0",
		12 => "SSL 2.0",
		_ => protocol.ToString(),
	};

	private static string NamesOf(CertificateInfo certificate)
	{
		List<string> names = [.. certificate.AlternativeNames
			.Where(name => name.Kind is GeneralNameKind.Dns or GeneralNameKind.IpAddress)
			.Select(name => name.Value)];
		if (names.Count == 0)
		{
			return certificate.Title;
		}

		return names.Count <= 6
			? string.Join(", ", names)
			: string.Create(CultureInfo.InvariantCulture, $"{string.Join(", ", names.Take(6))} and {names.Count - 6} more");
	}
}
