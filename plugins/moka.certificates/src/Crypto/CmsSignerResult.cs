namespace Moka.Certificates.Crypto;

/// <summary>One signer of a CMS signature and whether its signature holds.</summary>
public sealed record CmsSignerResult
{
	/// <summary>The signer's certificate subject, or how the signature names it when the certificate is not included.</summary>
	public required string Signer { get; init; }

	/// <summary>The signer certificate's SHA-256, when it is known.</summary>
	public string? CertificateSha256 { get; init; }

	public DateTimeOffset? SigningTime { get; init; }

	public required string DigestAlgorithm { get; init; }

	public required bool Valid { get; init; }

	/// <summary>Why it does not hold, in words.</summary>
	public string? Problem { get; init; }

	/// <summary>Whether the signer is the certificate that was asked for; null when none was.</summary>
	public bool? IsExpectedSigner { get; init; }
}
