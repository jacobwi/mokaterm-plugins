using Moka.Certificates.Crypto;

namespace Moka.Certificates.ServerFiles;

/// <summary>One certificate file on the server: where it is used, what was in it, and how its chain links up.</summary>
public sealed record ServerCertificateFile
{
	public required string Path { get; init; }

	public required ServerFileStatus Status { get; init; }

	public required IReadOnlyList<CertificateUse> Uses { get; init; }

	/// <summary>The certbot lineage the file belongs to, when certbot listed it.</summary>
	public CertbotCertificate? Lineage { get; init; }

	/// <summary>The certificates in the file, in order: the server's own first in a full chain.</summary>
	public IReadOnlyList<CertificateInfo> Certificates { get; init; } = [];

	/// <summary>How the file's certificates link up, when it holds more than one.</summary>
	public ChainCheck? Chain { get; init; }

	/// <summary>Why the file could not be read, in words.</summary>
	public string? Problem { get; init; }

	/// <summary>The first certificate, which is the server's own in a correctly ordered file.</summary>
	public CertificateInfo? Leaf => Certificates.Count > 0 ? Certificates[0] : null;
}
