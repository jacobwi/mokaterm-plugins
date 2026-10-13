using System.Globalization;
using Moka.DevTools.Components;
using Moka.DevTools.Data;
using Moka.DevTools.Text;
using Mokaterm.UI.Common.Components;

namespace Moka.DevTools.Tool;

/// <summary>
/// JSON formatted, minified and validated, JSON and YAML converted both ways, and two texts compared line by line. All
/// of it runs off the render thread and can be cancelled; errors carry their line and column.
/// </summary>
public sealed partial class DataSection : DevToolViewBase, IDisposable
{
	private const string FormatTab = "format";
	private const string ConvertTab = "convert";
	private const string DiffTab = "diff";

	private static readonly IReadOnlyList<(string Id, string Text)> Tabs = [(FormatTab, "Format"), (ConvertTab, "Convert"), (DiffTab, "Diff")];

	private static readonly IReadOnlyList<EnumOption<JsonIndent>> Indents =
	[
		new(JsonIndent.TwoSpaces, "2 spaces"),
		new(JsonIndent.FourSpaces, "4 spaces"),
		new(JsonIndent.Tab, "Tab"),
	];

	private static readonly IReadOnlyList<EnumOption<ConvertDirection>> Directions =
	[
		new(ConvertDirection.JsonToYaml, "JSON to YAML"),
		new(ConvertDirection.YamlToJson, "YAML to JSON"),
	];

	private string _tab = FormatTab;
	private TextValue _formatInput = TextValue.Empty;
	private TextValue _convertInput = TextValue.Empty;
	private TextValue _before = TextValue.Empty;
	private TextValue _after = TextValue.Empty;
	private JsonIndent _indent;
	private bool _sortKeys;
	private bool _diffAsJson;
	private ConvertDirection _direction;
	private DataResult? _result;
	private bool _busy;
	private CancellationTokenSource? _running;

	private string PrimaryText => _tab switch
	{
		FormatTab => "Format",
		ConvertTab => "Convert",
		_ => "Compare",
	};

	private string Summary => _tab switch
	{
		FormatTab => "format, minify, validate",
		ConvertTab => _direction == ConvertDirection.JsonToYaml ? "JSON to YAML" : "YAML to JSON",
		_ => "line diff",
	};

	private string ConvertHelp => _direction == ConvertDirection.JsonToYaml
		? "Strings that would read back as another type (true, 12, null, yes) are quoted, and text over several lines is written as a block."
		: "Plain values are typed by the YAML 1.2 core schema, so yes and on stay strings. Merge keys (<<) are applied and aliases expanded; comments are not kept.";

	public void Dispose()
	{
		Cancel();
		_formatInput = TextValue.Empty;
		_convertInput = TextValue.Empty;
		_before = TextValue.Empty;
		_after = TextValue.Empty;
		_result = null;
	}

	private static string Lines(string text)
	{
		int lines = text.Length == 0 ? 0 : text.Count(c => c == '\n') + (text[^1] == '\n' ? 0 : 1);
		return lines == 1 ? "1 line" : string.Create(CultureInfo.CurrentCulture, $"{lines:N0} lines");
	}

	private void OnTabChanged(string tab)
	{
		_tab = tab;
		_result = null;
	}

	private void OnDirectionChanged(ConvertDirection direction)
	{
		_direction = direction;
		_result = null;
	}

	private void UseAsInput(string text)
	{
		TextValue value = text.Length <= BoxChars ? new TextValue { Text = text } : new TextValue { Text = text, FileName = "output" };
		switch (_tab)
		{
			case FormatTab:
				_formatInput = value;
				break;
			case ConvertTab:
				_convertInput = value;
				_direction = _direction == ConvertDirection.JsonToYaml ? ConvertDirection.YamlToJson : ConvertDirection.JsonToYaml;
				break;
		}

		_result = null;
	}

	private Task RunPrimaryAsync() => _tab switch
	{
		FormatTab => RunAsync(_formatInput, (text, _) => Format(text, _indent, _sortKeys)),
		ConvertTab => RunAsync(_convertInput, (text, _) => Convert(text, _direction, _indent, _sortKeys)),
		_ => CompareAsync(),
	};

	private Task MinifyAsync()
	{
		bool sortKeys = _sortKeys;
		return RunAsync(_formatInput, (text, _) => new DataResult
		{
			Text = JsonTools.Minify(text, sortKeys),
			Title = "Minified JSON",
			FileName = "minified.json",
		});
	}

	private Task ValidateAsync() => RunAsync(_formatInput, (text, _) =>
	{
		JsonSummary summary = JsonTools.Validate(text);
		return new DataResult
		{
			VerdictChip = "valid",
			Verdict = $"Valid JSON: {summary.Description}.",
			Notes = summary.Warnings,
		};
	});

	private Task ValidateYamlAsync() => RunAsync(_convertInput, (text, _) =>
	{
		int documents = YamlConverter.Validate(text);
		return new DataResult
		{
			VerdictChip = "valid",
			Verdict = documents == 1 ? "Valid YAML: one document." : string.Create(CultureInfo.InvariantCulture, $"Valid YAML: {documents} documents."),
		};
	});

	private static DataResult Format(string text, JsonIndent indent, bool sortKeys) => new()
	{
		Text = JsonTools.Format(text, indent, sortKeys),
		Title = "Formatted JSON",
		FileName = "formatted.json",
	};

	private static DataResult Convert(string text, ConvertDirection direction, JsonIndent indent, bool sortKeys)
	{
		YamlConversion conversion = direction == ConvertDirection.JsonToYaml
			? YamlConverter.FromJson(text, sortKeys)
			: YamlConverter.ToJson(text, indent, sortKeys);
		return new DataResult
		{
			Text = conversion.Text,
			Title = direction == ConvertDirection.JsonToYaml ? "YAML" : "JSON",
			FileName = direction == ConvertDirection.JsonToYaml ? "converted.yaml" : "converted.json",
			Notes = conversion.Notes,
		};
	}

	private Task CompareAsync()
	{
		if (_before.IsEmpty && _after.IsEmpty)
		{
			_result = DataResult.Failed("nothing", "Paste or open the two texts to compare.");
			return Task.CompletedTask;
		}

		string before = _before.Text;
		string after = _after.Text;
		bool asJson = _diffAsJson;
		bool sortKeys = _sortKeys;
		if (before.Length > TextLimits.MaxTextChars || after.Length > TextLimits.MaxTextChars)
		{
			return TooLong();
		}

		return RunWorkAsync(token =>
		{
			string left = before;
			string right = after;
			if (asJson)
			{
				left = FormatSide(before, "Before", sortKeys);
				right = FormatSide(after, "After", sortKeys);
			}

			DiffResult diff = LineDiff.Compare(left, right, token);
			return diff switch
			{
				{ TooDifferent: true } => new DataResult
				{
					VerdictChip = "too different",
					Tone = ResultTone.Warning,
					Verdict = string.Create(CultureInfo.CurrentCulture, $"The texts are too different to compare line by line: more than {LineDiff.MaxChanges:N0} changed lines, or more work than a diff here is allowed."),
				},
				{ Identical: true } => new DataResult { VerdictChip = "same", Verdict = asJson ? "The two hold the same data, laid out the same way once formatted." : "The two texts are the same, line for line." },
				_ => new DataResult
				{
					VerdictChip = "differs",
					Tone = ResultTone.Warning,
					Verdict = string.Create(CultureInfo.CurrentCulture, $"{diff.Removed:N0} lines removed, {diff.Added:N0} added."),
					Diff = diff,
				},
			};
		});
	}

	private static string FormatSide(string text, string side, bool sortKeys)
	{
		try
		{
			return JsonTools.Format(text, JsonIndent.TwoSpaces, sortKeys);
		}
		catch (DataException ex)
		{
			throw new DataException($"{side} is not JSON: {ex.Message}");
		}
	}

	private Task RunAsync(TextValue input, Func<string, CancellationToken, DataResult> work)
	{
		if (input.IsEmpty)
		{
			_result = DataResult.Failed("nothing", "Paste or open something first.");
			return Task.CompletedTask;
		}

		string text = input.Text;
		return text.Length > TextLimits.MaxTextChars ? TooLong() : RunWorkAsync(token => work(text, token));
	}

	private Task TooLong()
	{
		_result = DataResult.Failed("too long", string.Create(CultureInfo.CurrentCulture, $"The text is longer than {TextLimits.MaxTextChars / (1024 * 1024)} MiB, the most this works on."));
		return Task.CompletedTask;
	}

	private async Task RunWorkAsync(Func<CancellationToken, DataResult> work)
	{
		Cancel();
		CancellationTokenSource running = new();
		_running = running;
		CancellationToken token = running.Token;
		_busy = true;
		_result = null;
		StateHasChanged();
		try
		{
			_result = await Task.Run(() => work(token), token);
		}
		catch (OperationCanceledException)
		{
			// A newer run cancels this one as well; only a run nobody replaced says it was stopped.
			if (_running is null || ReferenceEquals(_running, running))
			{
				_result = DataResult.Failed("cancelled", "Stopped before it finished.");
			}
		}
		catch (DataException ex)
		{
			_result = DataResult.Failed(ex.Position is null ? "invalid" : "error", ex.Message);
		}
		finally
		{
			if (ReferenceEquals(_running, running))
			{
				_running = null;
				_busy = false;
			}

			running.Dispose();
		}
	}

	private void Cancel()
	{
		if (Interlocked.Exchange(ref _running, null) is { } previous)
		{
			previous.Cancel();
		}

		_busy = false;
	}
}
