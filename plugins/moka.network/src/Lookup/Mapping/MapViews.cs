namespace Moka.Network.Lookup.Mapping;

/// <summary>
/// Everything the map has run so far: the question the name views share, each view's answer as it lands, and the sweep
/// with its own prefix. Immutable, so a run on a background thread replaces it whole and the renderer only ever sees
/// one state.
/// </summary>
public sealed record MapViews
{
	public required string Name { get; init; }

	public required int Type { get; init; }

	public DelegationWalkResult? Walk { get; init; }

	public CnameChainResult? Chain { get; init; }

	public ResolverMatrixResult? Matrix { get; init; }

	/// <summary>The prefix the sweep ran on, shown beside its answers.</summary>
	public string? SweepPrefix { get; init; }

	public PtrSweepResult? Sweep { get; init; }

	public bool Any => Walk is not null || Chain is not null || Matrix is not null || Sweep is not null;
}
