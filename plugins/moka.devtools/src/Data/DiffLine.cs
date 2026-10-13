namespace Moka.DevTools.Data;

/// <summary>
/// One row of a diff. Line numbers count from 1 and are 0 on the side a line is not on; a skipped row carries the
/// number of lines it stands for in <see cref="Count"/>.
/// </summary>
public sealed record DiffLine(DiffKind Kind, int OldNumber, int NewNumber, string Text, int Count = 1);
