namespace Moka.Certificates.ServerFiles;

/// <summary>One certificate lineage as <c>certbot certificates</c> lists it.</summary>
public sealed record CertbotCertificate
{
	/// <summary>The lineage name, which <c>certbot renew --cert-name</c> takes.</summary>
	public required string Name { get; init; }

	public IReadOnlyList<string> Domains { get; init; } = [];

	public string? KeyType { get; init; }

	public string? SerialNumber { get; init; }

	public DateTimeOffset? Expires { get; init; }

	/// <summary>What certbot said in brackets after the date, such as "VALID: 49 days" or "INVALID: EXPIRED".</summary>
	public string? State { get; init; }

	public string? CertificatePath { get; init; }

	public string? PrivateKeyPath { get; init; }

	/// <summary>certbot marked it as a staging (test) certificate, which no browser trusts.</summary>
	public bool IsTestCertificate => State?.Contains("TEST_CERT", StringComparison.OrdinalIgnoreCase) == true;
}
