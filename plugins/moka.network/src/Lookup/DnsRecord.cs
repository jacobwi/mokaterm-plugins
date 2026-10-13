namespace Moka.Network.Lookup;

/// <summary>
/// One resource record, its data already in presentation form (<c>10 mail.example.com</c>, <c>"v=spf1 -all"</c>). Data
/// that does not fit its type is kept as <c>\# length hex</c> and marked, so one bad record never hides the rest.
/// </summary>
public sealed record DnsRecord
{
	public required string Name { get; init; }

	public required int Type { get; init; }

	public required int Class { get; init; }

	/// <summary>Seconds a cache may keep the record.</summary>
	public required uint Ttl { get; init; }

	public required string Data { get; init; }

	/// <summary>The data did not have the shape its type calls for.</summary>
	public bool Malformed { get; init; }

	public string TypeName => DnsNames.TypeName(Type);
}
