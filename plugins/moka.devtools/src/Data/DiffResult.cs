namespace Moka.DevTools.Data;

/// <summary>The rows of a diff, or the verdict that the texts were too far apart to compare line by line.</summary>
public sealed record DiffResult(IReadOnlyList<DiffLine> Lines, int Added, int Removed, bool TooDifferent)
{
	public static DiffResult TooFarApart { get; } = new([], 0, 0, TooDifferent: true);

	public bool Identical => !TooDifferent && Added == 0 && Removed == 0;
}
