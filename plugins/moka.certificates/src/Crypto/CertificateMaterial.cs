using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>A certificate, without a private key attached: keys are separate items, so nothing lands in a key store.</summary>
public sealed class CertificateMaterial : MaterialItem
{
	public CertificateMaterial(X509Certificate2 certificate, string format)
		: base(MaterialKind.Certificate, format)
	{
		ArgumentNullException.ThrowIfNull(certificate);
		Certificate = certificate;
	}

	public X509Certificate2 Certificate { get; }

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Certificate.Dispose();
		}

		base.Dispose(disposing);
	}
}
