namespace Moka.DevTools.Dates;

/// <summary>A moment read from text, and how it was read.</summary>
public sealed record TimeReading
{
	public required DateTimeOffset Instant { get; init; }

	/// <summary>"Unix milliseconds", "RFC 3339", "ISO 8601", "RFC 1123".</summary>
	public required string Source { get; init; }

	/// <summary>The unit a number was read in; null for a date written out.</summary>
	public EpochUnit? Unit { get; init; }

	/// <summary>Why that unit, for a number whose unit was guessed: "13 digits".</summary>
	public string? UnitReason { get; init; }

	/// <summary>The offset the text gave, or null when it gave none and the time was read as UTC.</summary>
	public TimeSpan? WrittenOffset { get; init; }

	public IReadOnlyList<string> Notes { get; init; } = [];
}
