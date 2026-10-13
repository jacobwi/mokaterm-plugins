namespace Moka.Certificates.Crypto;

/// <summary>One object of an <see cref="Inspection"/>, described for the list and its detail panel.</summary>
public sealed record InspectedItem
{
	/// <summary>Its position in the input, from 1.</summary>
	public required int Number { get; init; }

	public required MaterialKind Kind { get; init; }

	public required string Format { get; init; }

	public required string Title { get; init; }

	public string? Subtitle { get; init; }

	public CertificateInfo? Certificate { get; init; }

	public RequestInfo? Request { get; init; }

	/// <summary>The key of a private or public key item.</summary>
	public KeyInfo? Key { get; init; }

	/// <summary>The public half of a key item as PEM, which is safe to show and copy.</summary>
	public string? PublicKeyPem { get; init; }

	/// <summary>An encrypted key or PFX that needs its password.</summary>
	public bool Locked { get; init; }

	/// <summary>Which other items a key belongs with, in words.</summary>
	public IReadOnlyList<string> Matches { get; init; } = [];
}
