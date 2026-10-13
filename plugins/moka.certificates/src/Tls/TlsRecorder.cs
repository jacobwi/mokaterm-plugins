using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Tls;

/// <summary>Copies what the validation callback is shown, since the chain it gets is disposed once it returns.</summary>
internal sealed class TlsRecorder : IDisposable
{
	private readonly List<X509Certificate2> _sent = [];
	private readonly List<X509Certificate2> _built = [];
	private X509ChainStatusFlags _status;
	private SslPolicyErrors _errors;
	private bool _handedOver;

	public bool Record(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
	{
		_errors = errors;
		if (certificate is not null)
		{
			byte[] leaf = certificate.GetRawCertData();
			_sent.Add(X509CertificateLoader.LoadCertificate(leaf));
			if (chain is not null)
			{
				// The certificates the server sent beyond its own; some platforms list the leaf here too.
				foreach (X509Certificate2 extra in chain.ChainPolicy.ExtraStore)
				{
					if (!extra.RawData.AsSpan().SequenceEqual(leaf))
					{
						_sent.Add(X509CertificateLoader.LoadCertificate(extra.RawData));
					}
				}
			}
		}

		if (chain is not null)
		{
			foreach (X509ChainElement element in chain.ChainElements)
			{
				_built.Add(X509CertificateLoader.LoadCertificate(element.Certificate.RawData));
			}

			foreach (X509ChainStatus status in chain.ChainStatus)
			{
				_status |= status.Status;
			}
		}

		return true;
	}

	public TlsObservation ToObservation(TlsTarget target, IPAddress? remote, SslProtocols protocol, TlsCipherSuite cipher, TimeSpan elapsed)
	{
		_handedOver = true;
		return new TlsObservation
		{
			Target = target,
			RemoteAddress = remote,
			Protocol = protocol,
			CipherSuite = cipher,
			SentCertificates = [.. _sent],
			BuiltChain = [.. _built],
			ChainStatus = _status,
			PolicyErrors = _errors,
			Elapsed = elapsed,
		};
	}

	public void Dispose()
	{
		if (_handedOver)
		{
			return;
		}

		foreach (X509Certificate2 certificate in _sent.Concat(_built))
		{
			certificate.Dispose();
		}
	}
}
