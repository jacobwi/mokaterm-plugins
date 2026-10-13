namespace Moka.Certificates.Crypto;

/// <summary>What the Inspect view shows for one input: each object, how a bundle's certificates link up, and the reader's notes.</summary>
public sealed record Inspection(IReadOnlyList<InspectedItem> Items, ChainCheck? Chain, IReadOnlyList<ReadNote> Notes)
{
	public static Inspection Empty { get; } = new([], null, []);
}
