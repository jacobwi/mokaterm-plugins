using System.Globalization;

namespace Moka.DevTools.Data;

/// <summary>
/// One run of changed lines with their context, as diff prints it: where it starts on each side, how many lines it
/// covers there, and how many unchanged lines were left out before it.
/// </summary>
public sealed record DiffHunk(int SkippedBefore, int OldStart, int OldCount, int NewStart, int NewCount, IReadOnlyList<DiffLine> Lines)
{
	/// <summary>The unified diff header: "@@ -12,7 +12,8 @@".</summary>
	public string Header => string.Create(CultureInfo.InvariantCulture, $"@@ -{OldStart},{OldCount} +{NewStart},{NewCount} @@");
}
