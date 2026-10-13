using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tests.Crypto;

public sealed class DerDetectorTests
{
	[Fact]
	public void Identify_EveryTypeByItsShape()
	{
		using RSA rsa = TestMaterial.RsaKey();
		using ECDsa ec = TestMaterial.EcKey();
		using X509Certificate2 certificate = TestMaterial.SelfSigned(ec);
		byte[] request = CertificateGenerator.CreateSigningRequest(ec, TestMaterial.Spec("x.example"));
		byte[] pfx = CertificateConverter.ToPfx(certificate, ec, [], new PfxOptions { Password = "p" });
		X509Certificate2Collection bundle = [certificate];

		Assert.Equal(DerType.Certificate, DerDetector.Identify(certificate.RawData));
		Assert.Equal(DerType.CertificateRequest, DerDetector.Identify(request));
		Assert.Equal(DerType.PrivateKey, DerDetector.Identify(rsa.ExportPkcs8PrivateKey()));
		Assert.Equal(DerType.EncryptedPrivateKey, DerDetector.Identify(ec.ExportEncryptedPkcs8PrivateKey("p", new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 10))));
		Assert.Equal(DerType.RsaPrivateKey, DerDetector.Identify(rsa.ExportRSAPrivateKey()));
		Assert.Equal(DerType.EcPrivateKey, DerDetector.Identify(ec.ExportECPrivateKey()));
		Assert.Equal(DerType.PublicKey, DerDetector.Identify(ec.ExportSubjectPublicKeyInfo()));
		Assert.Equal(DerType.RsaPublicKey, DerDetector.Identify(rsa.ExportRSAPublicKey()));
		Assert.Equal(DerType.Pfx, DerDetector.Identify(pfx));
		Assert.Equal(DerType.Pkcs7, DerDetector.Identify(bundle.Export(X509ContentType.Pkcs7)!));
	}

	[Theory]
	[InlineData(new byte[] { })]
	[InlineData(new byte[] { 0x04, 0x01, 0x00 })]
	[InlineData(new byte[] { 0x30, 0x00 })]
	[InlineData(new byte[] { 0x30, 0x03, 0x02, 0x01, 0x05, 0x00 })]
	[InlineData(new byte[] { 0x30, 0x80 })]
	public void Identify_AnythingElse_IsUnknown(byte[] der) => Assert.Equal(DerType.Unknown, DerDetector.Identify(der));

	[Fact]
	public void PemLabel_FollowsTheType()
	{
		Assert.Equal("CERTIFICATE", DerDetector.PemLabel(DerType.Certificate));
		Assert.Equal("EC PRIVATE KEY", DerDetector.PemLabel(DerType.EcPrivateKey));
		Assert.Null(DerDetector.PemLabel(DerType.Pfx));
	}
}
