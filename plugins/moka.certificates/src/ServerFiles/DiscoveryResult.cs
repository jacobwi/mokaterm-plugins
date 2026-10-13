namespace Moka.Certificates.ServerFiles;

/// <summary>What the discovery script found about where certificates are configured.</summary>
public sealed record DiscoveryResult
{
	public IReadOnlyList<CertbotCertificate> Lineages { get; init; } = [];

	/// <summary>certbot is installed but only answers root, and root mode is off.</summary>
	public bool CertbotNeedsRoot { get; init; }

	/// <summary>The folders under /etc/letsencrypt/live, each ending with a slash.</summary>
	public IReadOnlyList<string> LiveFolders { get; init; } = [];

	/// <summary>/etc/letsencrypt/live exists and only root can list it.</summary>
	public bool LiveNeedsRoot { get; init; }

	/// <summary>The certificate files nginx and Apache name.</summary>
	public IReadOnlyList<ConfiguredCertificate> Configured { get; init; } = [];

	/// <summary>Certificate settings that cannot be followed, such as a path built from a variable.</summary>
	public IReadOnlyList<string> Skipped { get; init; } = [];

	/// <summary>nginx is installed but <c>nginx -T</c> failed, which as a plain login usually means it could not read its keys.</summary>
	public bool NginxFailed { get; init; }

	/// <summary>The files the extra paths from settings came to, folders listed.</summary>
	public IReadOnlyList<string> ExtraFiles { get; init; } = [];
}
