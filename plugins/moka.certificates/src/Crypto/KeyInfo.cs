namespace Moka.Certificates.Crypto;

/// <summary>A public key described for the user. Private keys are described by their public half.</summary>
public sealed record KeyInfo
{
	/// <summary>"RSA", "ECDSA", "Ed25519", or the algorithm's number for one this tool has no name for.</summary>
	public required string Algorithm { get; init; }

	/// <summary>The key size in bits, when the algorithm has one.</summary>
	public int? Bits { get; init; }

	/// <summary>The named curve of an EC key, such as "P-256".</summary>
	public string? Curve { get; init; }

	/// <summary>One line: "RSA 2048", "ECDSA P-256".</summary>
	public required string Description { get; init; }

	/// <summary>SHA-256 over the SubjectPublicKeyInfo, as colon-separated hex.</summary>
	public required string SpkiSha256 { get; init; }

	/// <summary>The same hash in base64, the form key pins (HPKP, curl's --pinnedpubkey) are written in.</summary>
	public required string Pin { get; init; }

	/// <summary>An RSA or ECDSA key, which this tool can sign, verify and generate with. Ed25519 and others are only described.</summary>
	public bool Usable { get; init; }

	/// <summary>Why the key cannot be used here, when it cannot.</summary>
	public string? Limitation { get; init; }
}
