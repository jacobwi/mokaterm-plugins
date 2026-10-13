using Moka.Certificates.ServerFiles;
using Mokaterm.Abstractions.ServerAdmin;

namespace Moka.Certificates.Tests.ServerFiles;

public sealed class ServerCertificateCommandsTests
{
	[Fact]
	public void Discover_QuotesEveryExtraPathAndAsksOnlyWhatIsInstalled()
	{
		ServerFacts server = Server("nginx");

		SystemScript script = ServerCertificateCommands.Discover(server, ["/opt/my certs", "/etc/x'y.pem"], asRoot: false);

		Assert.Equal("certificates.discover", script.Name);
		Assert.True(SystemScript.IsName(script.Name));
		Assert.False(script.AsRoot);
		Assert.Contains("nginx -T", script.Body, StringComparison.Ordinal);
		Assert.DoesNotContain("certbot certificates", script.Body, StringComparison.Ordinal);
		Assert.Contains("'/opt/my certs'/*.pem", script.Body, StringComparison.Ordinal);
		Assert.Contains(@"'/etc/x'\''y.pem'", script.Body, StringComparison.Ordinal);
		Assert.Throws<ArgumentException>(() => ServerCertificateCommands.Discover(server, ["relative/path"], asRoot: false));
	}

	[Fact]
	public void Discover_AsksCertbotOnlyAsRoot()
	{
		SystemScript script = ServerCertificateCommands.Discover(Server("certbot"), [], asRoot: true);

		Assert.True(script.AsRoot);
		Assert.Contains("if [ \"$(id -u)\" = 0 ]; then certbot certificates 2>&1;", script.Body, StringComparison.Ordinal);
	}

	[Fact]
	public void ReadFiles_FiltersWithAwkOrSedAndQuotesThePaths()
	{
		SystemScript withAwk = ServerCertificateCommands.ReadFiles(Server("awk", "base64"), ["/etc/a b.pem", "/etc/c.der"], asRoot: true);
		SystemScript withSed = ServerCertificateCommands.ReadFiles(Server(), ["/etc/c.pem"], asRoot: false);

		Assert.Contains("awk '/^-----BEGIN /", withAwk.Body, StringComparison.Ordinal);
		Assert.Contains("base64 < \"$2\"", withAwk.Body, StringComparison.Ordinal);
		Assert.Contains("mc_file 0 '/etc/a b.pem'", withAwk.Body, StringComparison.Ordinal);
		Assert.Contains("mc_file 1 '/etc/c.der'", withAwk.Body, StringComparison.Ordinal);
		Assert.Contains("sed -n '/^-----BEGIN CERTIFICATE-----/,/^-----END CERTIFICATE-----/p'", withSed.Body, StringComparison.Ordinal);
		Assert.DoesNotContain("base64 <", withSed.Body, StringComparison.Ordinal);
		Assert.Throws<ArgumentException>(() => ServerCertificateCommands.ReadFiles(Server(), ["/etc/bad\npath"], false));
		Assert.Throws<ArgumentException>(() => ServerCertificateCommands.ReadFiles(Server(), [.. Enumerable.Repeat("/a", ServerCertificateCommands.MaxFiles + 1)], false));
	}

	[Fact]
	public void Changes_RunAsRootWithTheLineageQuoted()
	{
		SystemChange all = ServerCertificateCommands.RenewDryRun(null);
		SystemChange one = ServerCertificateCommands.RenewDryRun("example.com");
		SystemChange renew = ServerCertificateCommands.Renew("example.com-0001");

		Assert.Equal("certbot renew --dry-run", all.Script.Body);
		Assert.Equal("certbot renew --dry-run --cert-name 'example.com'", one.Script.Body);
		Assert.Equal("certbot renew --cert-name 'example.com-0001'", renew.Script.Body);
		Assert.All(new[] { all, one, renew }, change => Assert.True(change.Script.AsRoot));
		Assert.All(new[] { all, one, renew }, change => Assert.True(SystemScript.IsName(change.Script.Name)));
		Assert.Throws<ArgumentException>(() => ServerCertificateCommands.Renew("x; rm -rf /"));
		Assert.Throws<ArgumentException>(() => ServerCertificateCommands.RenewDryRun("../etc"));
	}

	[Fact]
	public void Reloads_TestTheConfigurationFirst()
	{
		ServerFacts systemd = Server("systemctl", "apache2ctl") with { InitSystem = "systemd" };
		ServerFacts openrc = Server("apachectl") with { InitSystem = "openrc" };

		Assert.Equal("nginx -t && systemctl reload nginx", ServerCertificateCommands.ReloadNginx(systemd).Script.Body);
		Assert.Equal("nginx -t && nginx -s reload", ServerCertificateCommands.ReloadNginx(openrc).Script.Body);
		Assert.Equal("apache2ctl configtest && systemctl reload apache2", ServerCertificateCommands.ReloadApache(systemd)!.Script.Body);
		Assert.Equal("apachectl configtest && apachectl graceful", ServerCertificateCommands.ReloadApache(openrc)!.Script.Body);
		Assert.Null(ServerCertificateCommands.ReloadApache(Server()));
	}

	[Theory]
	[InlineData("/etc/ssl/a.pem", true)]
	[InlineData("/etc/ssl/with space/a.pem", true)]
	[InlineData("etc/a.pem", false)]
	[InlineData("", false)]
	[InlineData("/etc/a\tb", false)]
	public void PathProblem_TakesAbsolutePathsWithoutControlCharacters(string path, bool fine) =>
		Assert.Equal(fine, ServerCertificateCommands.PathProblem(path) is null);

	[Theory]
	[InlineData("example.com", true)]
	[InlineData("example.com-0001", true)]
	[InlineData("my_cert", true)]
	[InlineData("-flag", false)]
	[InlineData("a b", false)]
	[InlineData("a..b", false)]
	[InlineData("$(id)", false)]
	public void LineageProblem_TakesWhatCertbotNames(string name, bool fine) =>
		Assert.Equal(fine, ServerCertificateCommands.LineageProblem(name) is null);

	[Fact]
	public void Probed_AreAllCommandNamesTheViewCanLookFor() =>
		Assert.All(ServerCertificateCommands.Probed, command => Assert.True(ServerFacts.IsCommandName(command)));

	internal static ServerFacts Server(params string[] commands) => new()
	{
		DisplayName = "Ubuntu 24.04 LTS",
		Commands = new HashSet<string>(commands, StringComparer.Ordinal),
	};
}
