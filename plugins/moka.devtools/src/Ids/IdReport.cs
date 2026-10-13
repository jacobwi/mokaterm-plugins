namespace Moka.DevTools.Ids;

/// <summary>A UUID or ULID taken apart: what it is, the time inside when it has one, and the same bits in the other form.</summary>
public sealed record IdReport
{
	/// <summary>"UUID" or "ULID".</summary>
	public required string Kind { get; init; }

	/// <summary>The form it is usually written in: lower case 8-4-4-4-12 for a UUID, 26 capitals for a ULID.</summary>
	public required string Canonical { get; init; }

	/// <summary>"v7, Unix time and random", "Nil UUID", "ULID".</summary>
	public required string Summary { get; init; }

	/// <summary>The UUID version, when the variant gives it one.</summary>
	public int? Version { get; init; }

	public string? Variant { get; init; }

	/// <summary>The moment inside, for v1, v6, v7 and ULIDs.</summary>
	public DateTimeOffset? Time { get; init; }

	public IReadOnlyList<IdFact> Facts { get; init; } = [];

	public IReadOnlyList<string> Notes { get; init; } = [];
}
