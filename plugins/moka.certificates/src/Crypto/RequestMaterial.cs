using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>A certificate signing request (PKCS#10), with its bytes as they were, which its signature covers.</summary>
public sealed class RequestMaterial : MaterialItem
{
	public RequestMaterial(CertificateRequest request, byte[] der, string format)
		: base(MaterialKind.CertificateRequest, format)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(der);
		Request = request;
		Der = der;
	}

	public CertificateRequest Request { get; }

	public byte[] Der { get; }
}
