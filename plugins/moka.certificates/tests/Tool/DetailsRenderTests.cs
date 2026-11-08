using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Moka.Certificates.Components;
using Moka.Certificates.Crypto;
using Moka.Certificates.Tool;

namespace Moka.Certificates.Tests.Tool;

/// <summary>The certificate, key and request views and the chain, drawn from material the tests make, with nothing clicked.</summary>
public sealed class DetailsRenderTests
{
	[Fact]
	public async Task CertificateDetails_ShowsTheHeadlineTheGaugeTheFactCardsAndTheExtensions()
	{
		using ECDsa key = TestMaterial.EcKey();
		using X509Certificate2 certificate = TestMaterial.SelfSigned(key, "shop.example.com", "shop.example.com", "192.0.2.10");
		CertificateInfo info = CertificateReader.Describe(certificate, TestMaterial.Now, 30);

		string html = await Rendering.RenderAsync<CertificateDetails>(new() { [nameof(CertificateDetails.Info)] = info });

		Assert.Contains("moka-gauge", html, StringComparison.Ordinal);
		Assert.Contains("Share of its validity left", html, StringComparison.Ordinal);
		Assert.Contains("moka-stat--tile", html, StringComparison.Ordinal);
		foreach (string label in new[] { "Subject", "Days left", "Key", "Signature", "Issuer", "Validity", "Names", "Key and signature", "Fingerprints", "Extensions" })
		{
			Assert.Contains($">{label}<", html, StringComparison.Ordinal);
		}

		Assert.Contains($">{info.DaysLeft.ToString("N0", CultureInfo.CurrentCulture)}<", html, StringComparison.Ordinal);
		Assert.Contains("Self-signed", html, StringComparison.Ordinal);
		Assert.Contains(info.Sha256, html, StringComparison.Ordinal);
		Assert.Contains("Copy the SHA-256 fingerprint", html, StringComparison.Ordinal);
		Assert.Contains("192.0.2.10", html, StringComparison.Ordinal);
		Assert.Contains("moka-table", html, StringComparison.Ordinal);
		Assert.Contains("Subject alternative names", html, StringComparison.Ordinal);
		Assert.Contains("Copy PEM", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task CertificateDetails_AnExpiredCertificate_CountsTheDaysSince()
	{
		using ECDsa key = TestMaterial.EcKey();
		using X509Certificate2 certificate = TestMaterial.SelfSigned(key);
		CertificateInfo info = CertificateReader.Describe(certificate, TestMaterial.Now.AddDays(400), 30);

		string html = await Rendering.RenderAsync<CertificateDetails>(new() { [nameof(CertificateDetails.Info)] = info, [nameof(CertificateDetails.ShowExports)] = false });

		Assert.Contains(">Expired<", html, StringComparison.Ordinal);
		Assert.Contains("Days since it expired", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Copy PEM", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ChainView_AChainInOrder_JudgesEveryCertificate()
	{
		using TestChain chain = TestMaterial.Chain();
		X509Certificate2[] certificates = [chain.Leaf, chain.Intermediate, chain.Root];

		string html = await Rendering.RenderAsync<ChainView>(new()
		{
			[nameof(ChainView.Certificates)] = Describe(certificates),
			[nameof(ChainView.Check)] = ChainCheck.Analyze(certificates),
		});

		Assert.Contains("moka-chain-check", html, StringComparison.Ordinal);
		foreach (string word in new[] { "Leaf", "Intermediate", "Root", "Valid", "Verified", "Self-signed" })
		{
			Assert.Contains(word, html, StringComparison.Ordinal);
		}

		Assert.DoesNotContain("Not the issuer", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ChainView_OutOfOrderAndWithoutItsRoot_SaysWhereItBreaks()
	{
		using TestChain chain = TestMaterial.Chain();
		X509Certificate2[] outOfOrder = [chain.Intermediate, chain.Leaf];
		X509Certificate2[] noRoot = [chain.Leaf, chain.Intermediate];

		string broken = await Rendering.RenderAsync<ChainView>(new()
		{
			[nameof(ChainView.Certificates)] = Describe(outOfOrder),
			[nameof(ChainView.Check)] = ChainCheck.Analyze(outOfOrder),
		});
		string served = await Rendering.RenderAsync<ChainView>(new()
		{
			[nameof(ChainView.Certificates)] = Describe(noRoot),
			[nameof(ChainView.Check)] = ChainCheck.Analyze(noRoot),
		});

		Assert.Contains("Not the issuer", broken, StringComparison.Ordinal);
		Assert.Contains("Not included", served, StringComparison.Ordinal);
		Assert.Contains("which is what a server should send", served, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Inspect_AChainAndItsKey_ListsThemShowsTheChainAndTheFirstCertificate()
	{
		using TestChain chain = TestMaterial.Chain(rsa: true);
		string pem = TestMaterial.Pem(chain.Leaf, chain.Intermediate, chain.Root) + CertificateConverter.PrivateKeyPem(chain.LeafKey, PrivateKeyFormat.Pkcs8);

		string html = await Rendering.RenderAsync<InspectSection>(new() { [nameof(InspectSection.Request)] = new InspectRequest(pem, 1) });

		Assert.Contains("3 certificates, 1 private key", html, StringComparison.Ordinal);
		Assert.Contains("What it holds", html, StringComparison.Ordinal);
		Assert.Contains("CA certificate", html, StringComparison.Ordinal);
		Assert.Contains("Private key", html, StringComparison.Ordinal);
		Assert.Contains("moka-chain-check", html, StringComparison.Ordinal);
		Assert.Contains("In order", html, StringComparison.Ordinal);
		Assert.Contains("Belongs with", html, StringComparison.Ordinal);
		Assert.Contains("Item 1, PEM", html, StringComparison.Ordinal);
		Assert.Contains("Days left", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Inspect_ASigningRequest_ShowsWhatItAsksFor()
	{
		using ECDsa key = TestMaterial.EcKey();
		byte[] request = CertificateGenerator.CreateSigningRequest(key, TestMaterial.Spec("api.example.com", names: ["api.example.com"]));

		string html = await Rendering.RenderAsync<InspectSection>(new() { [nameof(InspectSection.Request)] = new InspectRequest(CertificateConverter.RequestPem(request), 1) });

		Assert.Contains("Signature ok", html, StringComparison.Ordinal);
		Assert.Contains("Names asked for", html, StringComparison.Ordinal);
		Assert.Contains("api.example.com", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Inspect_APublicKey_ShowsItsSizeAndItsPem()
	{
		using RSA key = TestMaterial.RsaKey();

		string html = await Rendering.RenderAsync<InspectSection>(new() { [nameof(InspectSection.Request)] = new InspectRequest(CertificateConverter.PublicKeyPem(new PublicKey(key)), 1) });

		Assert.Contains("Public key", html, StringComparison.Ordinal);
		Assert.Contains(2048.ToString("N0", CultureInfo.CurrentCulture), html, StringComparison.Ordinal);
		Assert.Contains("Stored as", html, StringComparison.Ordinal);
		Assert.Contains("-----BEGIN PUBLIC KEY-----", html, StringComparison.Ordinal);
		Assert.Contains("moka-code-block", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-code-block__copy", html, StringComparison.Ordinal);
	}

	private static List<CertificateInfo> Describe(IEnumerable<X509Certificate2> certificates) =>
		[.. certificates.Select(certificate => CertificateReader.Describe(certificate, TestMaterial.Now, 30))];
}
