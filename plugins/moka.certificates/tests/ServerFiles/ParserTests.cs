using Moka.Certificates.ServerFiles;

namespace Moka.Certificates.Tests.ServerFiles;

public sealed class ParserTests
{
	[Fact]
	public void Certbot_ReadsEveryLineage()
	{
		IReadOnlyList<CertbotCertificate> lineages = CertbotOutput.Parse(Fixtures.Certbot);

		Assert.Equal(2, lineages.Count);
		CertbotCertificate first = lineages[0];
		Assert.Equal("example.com", first.Name);
		Assert.Equal(["example.com", "www.example.com"], first.Domains);
		Assert.Equal("ECDSA", first.KeyType);
		Assert.Equal("3c9d1a2b4e5f60718293a4b5c6d7e8f9a0b", first.SerialNumber);
		Assert.Equal(new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero), first.Expires);
		Assert.Equal("VALID: 49 days", first.State);
		Assert.Equal("/etc/letsencrypt/live/example.com/fullchain.pem", first.CertificatePath);
		Assert.Equal("INVALID: EXPIRED", lineages[1].State);
	}

	[Fact]
	public void Certbot_OldOutputAndNothingAtAll()
	{
		CertbotCertificate legacy = Assert.Single(CertbotOutput.Parse(Fixtures.OldCertbot));

		Assert.Null(legacy.KeyType);
		Assert.Null(legacy.SerialNumber);
		Assert.True(legacy.IsTestCertificate);
		Assert.Empty(CertbotOutput.Parse("Saving debug log to /var/log/letsencrypt/letsencrypt.log\nNo certificates found.\n"));
	}

	[Fact]
	public void Nginx_FindsEveryCertificateWithItsServerNames()
	{
		(IReadOnlyList<ConfiguredCertificate> found, IReadOnlyList<string> skipped) = NginxConfig.Parse(Fixtures.NginxDump);

		Assert.Equal(
			[
				new ConfiguredCertificate("/etc/ssl/mail/imap.pem", "nginx stream", "/etc/nginx/nginx.conf", [], "ssl_certificate"),
				new ConfiguredCertificate("/etc/letsencrypt/live/example.com/fullchain.pem", "nginx", "/etc/nginx/sites-enabled/example", ["example.com", "www.example.com"], "ssl_certificate"),
				new ConfiguredCertificate("/etc/nginx/certs/api.crt", "nginx", "/etc/nginx/sites-enabled/example", ["api.example.com"], "ssl_certificate"),
				new ConfiguredCertificate("/etc/ssl/certs/default.pem", "nginx", "/etc/nginx/nginx.conf", [], "ssl_certificate"),
			],
			found,
			new ConfiguredComparer());
		Assert.Contains("$ssl_server_name.crt", Assert.Single(skipped), StringComparison.Ordinal);
	}

	[Fact]
	public void Nginx_NothingOrBrokenInput_FindsNothing()
	{
		Assert.Empty(NginxConfig.Parse("").Certificates);
		Assert.Empty(NginxConfig.Parse("server { ssl_certificate \"/unterminated").Certificates);
	}

	[Fact]
	public void Apache_FindsCertificatesPerVirtualHost()
	{
		IReadOnlyList<ConfiguredCertificate> found = ApacheConfig.Parse(Fixtures.ApacheGrep);

		Assert.Equal(
		[
			new ConfiguredCertificate("/etc/ssl/certs/ssl-cert-snakeoil.pem", "Apache", "/etc/apache2/sites-enabled/default-ssl.conf", ["www.example.org", "example.org"], "SSLCertificateFile"),
			new ConfiguredCertificate("/etc/ssl/shop/shop.crt", "Apache", "/etc/apache2/sites-enabled/shop.conf", ["shop.example.org"], "SSLCertificateFile"),
			new ConfiguredCertificate("/etc/apache2/ssl/chain.pem", "Apache", "/etc/apache2/sites-enabled/shop.conf", ["shop.example.org"], "SSLCertificateChainFile"),
			new ConfiguredCertificate("/etc/pki/tls/certs/localhost.crt", "Apache", "/etc/httpd/conf.d/ssl.conf", [], "SSLCertificateFile"),
		],
			found,
			new ConfiguredComparer());
	}

	[Fact]
	public void Caddy_TakesTlsCertPathsAndSaysWhatItCannotFollow()
	{
		(IReadOnlyList<ConfiguredCertificate> found, IReadOnlyList<string> skipped) = CaddyConfig.Parse(Fixtures.CaddyGrep);

		Assert.Equal(
		[
			new ConfiguredCertificate("/etc/ssl/example.com.cert.pem", "Caddy", "/etc/caddy/Caddyfile", [], "tls"),
			new ConfiguredCertificate("/etc/ssl/quoted.pem", "Caddy", "/etc/caddy/Caddyfile", [], "tls"),
		],
			found,
			new ConfiguredComparer());
		Assert.Contains("chosen at runtime", Assert.Single(skipped), StringComparison.Ordinal);
	}

	[Fact]
	public void HAProxy_TakesCrtFilesAndEveryCrtListEntry()
	{
		IReadOnlyList<ConfiguredCertificate> found = HAProxyConfig.Parse(Fixtures.HAProxyPart);

		Assert.Equal(
		[
			new ConfiguredCertificate("/etc/haproxy/certs/site.pem", "HAProxy", "/etc/haproxy/haproxy.cfg", [], "crt"),
			new ConfiguredCertificate("/etc/haproxy/certs/other.pem", "HAProxy", "/etc/haproxy/haproxy.cfg", [], "crt"),
			new ConfiguredCertificate("/etc/haproxy/certs/shop.example.org.pem", "HAProxy", "/etc/haproxy/certs/list.txt", [], "crt-list"),
			new ConfiguredCertificate("/etc/haproxy/certs/api.example.org.pem", "HAProxy", "/etc/haproxy/certs/list.txt", [], "crt-list"),
		],
			found,
			new ConfiguredComparer());
	}

	[Fact]
	public void HAProxy_NothingOrNoise_FindsNothing()
	{
		Assert.Empty(HAProxyConfig.Parse(""));
		Assert.Empty(HAProxyConfig.Parse("some noise without a path\n12:no digits before the colon\n"));
	}

	[Fact]
	public void Lighttpd_TakesPemFilesAndSaysWhatIsRelative()
	{
		(IReadOnlyList<ConfiguredCertificate> found, IReadOnlyList<string> skipped) = LighttpdConfig.Parse(Fixtures.LighttpdGrep);

		Assert.Equal(
		[
			new ConfiguredCertificate("/etc/lighttpd/server.pem", "lighttpd", "/etc/lighttpd/lighttpd.conf", [], "ssl.pemfile"),
			new ConfiguredCertificate("/etc/lighttpd/shop.pem", "lighttpd", "/etc/lighttpd/conf-enabled/90-cert.conf", [], "ssl.pemfile"),
		],
			found,
			new ConfiguredComparer());
		Assert.Contains("relative", Assert.Single(skipped), StringComparison.Ordinal);
	}

	[Fact]
	public void Traefik_TakesCertFileValuesInYamlAndToml()
	{
		IReadOnlyList<ConfiguredCertificate> found = TraefikConfig.Parse(Fixtures.TraefikGrep);

		Assert.Equal(
		[
			new ConfiguredCertificate("/etc/traefik/certs/example.com.crt", "Traefik", "/etc/traefik/dynamic/certs.yml", [], "certFile"),
			new ConfiguredCertificate("/etc/traefik/certs/toml-site.crt", "Traefik", "/etc/traefik/traefik.toml", [], "certFile"),
			new ConfiguredCertificate("/etc/traefik/dynamic/certs/relative.crt", "Traefik", "/etc/traefik/dynamic/relative.yml", [], "certFile"),
		],
			found,
			new ConfiguredComparer());
	}

	[Fact]
	public void Traefik_KeyFileLinesAreNotGrepped()
	{
		Assert.Empty(TraefikConfig.Parse("/etc/traefik/dynamic/certs.yml:6:  keyFile: /etc/traefik/certs/example.com.key\n"));
	}

	[Fact]
	public void Discovery_SplitsEveryPart()
	{
		DiscoveryResult result = ScriptOutput.ParseDiscovery(Fixtures.Discovery);

		Assert.Equal(2, result.Lineages.Count);
		Assert.Equal(["/etc/letsencrypt/live/example.com/", "/etc/letsencrypt/live/old.example.org/"], result.LiveFolders);
		Assert.Equal(19, result.Configured.Count);
		Assert.Equal(3, result.Skipped.Count);
		Assert.Equal(["/opt/app/tls/server.crt"], result.ExtraFiles);
		Assert.False(result.CertbotNeedsRoot);
		Assert.False(result.NginxFailed);
	}

	[Fact]
	public void Discovery_AsAPlainLogin_SaysWhatNeedsRoot()
	{
		DiscoveryResult result = ScriptOutput.ParseDiscovery(Fixtures.DiscoveryAsLogin);

		Assert.True(result.CertbotNeedsRoot);
		Assert.True(result.LiveNeedsRoot);
		Assert.True(result.NginxFailed);
		Assert.Empty(result.Lineages);
		Assert.Empty(result.Configured);
	}

	[Fact]
	public void Discovery_ALineThatOnlyLooksLikeAMarker_IsContent()
	{
		DiscoveryResult result = ScriptOutput.ParseDiscovery("@@mc:extra\n@@mc:extra-ish\n/a/b.pem\n@@mc:end\n");

		Assert.Equal(["@@mc:extra-ish", "/a/b.pem"], result.ExtraFiles);
	}

	[Fact]
	public void Files_AreSplitByIndex()
	{
		IReadOnlyDictionary<int, FileRead> files = ScriptOutput.ParseFiles("""
			banner noise
			@@mc:file 0 pem
			-----BEGIN CERTIFICATE-----
			AAAA
			-----END CERTIFICATE-----
			@@mc:file 1 missing
			@@mc:file 2 der
			MIIB
			@@mc:file x pem
			@@mc:file 3 unreadable
			""");

		Assert.Equal([0, 1, 2, 3], files.Keys.Order());
		Assert.Equal("pem", files[0].State);
		Assert.StartsWith("-----BEGIN CERTIFICATE-----\nAAAA\n", files[0].Content, StringComparison.Ordinal);
		Assert.Equal("missing", files[1].State);
		Assert.Contains("@@mc:file x pem", files[2].Content, StringComparison.Ordinal);
		Assert.Equal("unreadable", files[3].State);
	}

	private sealed class ConfiguredComparer : IEqualityComparer<ConfiguredCertificate>
	{
		public bool Equals(ConfiguredCertificate? x, ConfiguredCertificate? y) =>
			x is not null && y is not null
			&& x.Path == y.Path && x.Server == y.Server && x.ConfigFile == y.ConfigFile && x.Directive == y.Directive
			&& x.ServerNames.SequenceEqual(y.ServerNames);

		public int GetHashCode(ConfiguredCertificate obj) => obj.Path.GetHashCode(StringComparison.Ordinal);
	}
}
