using System.Text;
using Moka.DevTools.Components;
using Moka.DevTools.Data;

namespace Moka.DevTools.Tool;

/// <summary>What one run of the JSON and YAML section made: text, a verdict, a diff, or an error with its place.</summary>
public sealed record DataResult
{
	public string? Text { get; init; }

	/// <summary>What the output is called on its box and when saved: "Formatted JSON", "formatted.json".</summary>
	public string Title { get; init; } = "Output";

	public string? FileName { get; init; }

	public string? Verdict { get; init; }

	public string? VerdictChip { get; init; }

	public ResultTone Tone { get; init; } = ResultTone.Good;

	public IReadOnlyList<string> Notes { get; init; } = [];

	public DiffResult? Diff { get; init; }

	public static DataResult Failed(string chip, string message) => new() { VerdictChip = chip, Verdict = message, Tone = ResultTone.Bad };

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Title = ").Append(Title).Append(", Text = ").Append(Text?.Length ?? 0).Append(" chars");
		return true;
	}
}
