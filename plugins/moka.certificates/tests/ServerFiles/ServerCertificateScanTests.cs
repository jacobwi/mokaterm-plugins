using Moka.Certificates.Crypto;
using Moka.Certificates.ServerFiles;

namespace Moka.Certificates.Tests.ServerFiles;

public sealed class ServerCertificateScanTests
{
	[Fact]
	public void Candidates_MergeEveryUseOfAFile()
	{
		DiscoveryResult discovery = ScriptOutput.ParseDiscovery(Fixtures.Discovery);

		IReadOnlyList<ScanCandidate> candidates = ServerCertificateScan.Candidates(discovery);

		Assert.Equal(
			[
				"/etc/letsencrypt/live/example.com/fullchain.pem",
				"/etc/letsencrypt/live/old.example.org/fullchain.pem",
				"/etc/ssl/mail/imap.pem",
				"/etc/nginx/certs/api.crt",
				"/etc/ssl/certs/default.pem",
				"/etc/ssl/certs/ssl-cert-snakeoil.pem",
				"/etc/ssl/shop/shop.crt",
				"/etc/apache2/ssl/chain.pem",
				"/etc/pki/tls/certs/localhost.crt",
				"/opt/app/tls/server.crt",
			],
			candidates.Select(candidate => candidate.Path));
		ScanCandidate example = candidates[0];
		Assert.Equal(["certbot", "nginx"], example.Uses.Select(use => use.Source));
		Assert.Equal("example.com: example.com www.example.com", example.Uses[0].Detail);
		Assert.Contains("example.com www.example.com", example.Uses[1].Detail, StringComparison.Ordinal);
		Assert.Equal("ECDSA", example.Lineage?.KeyType);
		Assert.Contains("every server without its own", candidates[4].Uses[0].Detail, StringComparison.Ordinal);
		Assert.Equal("Settings", candidates[^1].Uses[0].Source);
	}

	[Fact]
	public void Candidates_InferTheLineageFromALiveFolder()
	{
		DiscoveryResult discovery = new()
		{
			Configured = [new ConfiguredCertificate("/etc/letsencrypt/live/blog.example/fullchain.pem", "nginx", "/etc/nginx/sites-enabled/blog", ["blog.example"], "ssl_certificate")],
		};

		ScanCandidate candidate = Assert.Single(ServerCertificateScan.Candidates(discovery));

		Assert.Equal("blog.example", candidate.Lineage?.Name);
		Assert.Null(ServerCertificateScan.LineageName("/etc/ssl/a.pem"));
		Assert.Null(ServerCertificateScan.LineageName("/etc/letsencrypt/live/../../etc/x"));
	}

	[Fact]
	public void Build_TurnsEveryStateIntoARow()
	{
		using TestChain chain = TestMaterial.Chain();
		ScanCandidate[] candidates = [.. Enumerable.Range(0, 8).Select(index => new ScanCandidate($"/etc/ssl/{index}.pem", [new CertificateUse("Settings", "x")], null))];
		Dictionary<int, FileRead> reads = new()
		{
			[0] = new("pem", TestMaterial.Pem(chain.Leaf, chain.Intermediate)),
			[1] = new("der", Convert.ToBase64String(chain.Root.RawData, Base64FormattingOptions.InsertLineBreaks)),
			[2] = new("missing", ""),
			[3] = new("unreadable", ""),
			[4] = new("folder", ""),
			[5] = new("other", ""),
			[6] = new("pem", CertificateConverter.PrivateKeyPem(chain.LeafKey, PrivateKeyFormat.Pkcs8)),
		};

		IReadOnlyList<ServerCertificateFile> files = ServerCertificateScan.Build(candidates, reads, readAsRoot: false, TestMaterial.Now, 30);

		Assert.Equal(
			[ServerFileStatus.Read, ServerFileStatus.Read, ServerFileStatus.Missing, ServerFileStatus.Unreadable, ServerFileStatus.Folder, ServerFileStatus.NoCertificate, ServerFileStatus.NoCertificate, ServerFileStatus.NotRead],
			files.Select(file => file.Status));
		Assert.Equal("www.example.com", files[0].Leaf?.Title);
		Assert.Equal(2, files[0].Certificates.Count);
		Assert.True(files[0].Chain?.InOrder);
		Assert.Equal("Moka Test Root", files[1].Leaf?.Title);
		Assert.Contains("Turn root mode on", files[3].Problem, StringComparison.Ordinal);
		Assert.Empty(files[6].Certificates);
	}

	[Fact]
	public void Build_SameReadAsRoot_SaysRootCouldNotEither()
	{
		ScanCandidate[] candidates = [new("/root/x.pem", [], null)];

		ServerCertificateFile file = Assert.Single(ServerCertificateScan.Build(candidates, new Dictionary<int, FileRead> { [0] = new("unreadable", "") }, readAsRoot: true, TestMaterial.Now, 30));

		Assert.Equal("The file cannot be read, even as root.", file.Problem);
	}
}
