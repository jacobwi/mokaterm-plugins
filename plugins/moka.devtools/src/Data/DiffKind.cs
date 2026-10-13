namespace Moka.DevTools.Data;

public enum DiffKind
{
	Same,
	Removed,
	Added,

	/// <summary>A run of unchanged lines left out between two changes.</summary>
	Skipped,
}
