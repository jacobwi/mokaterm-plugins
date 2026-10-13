using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.DevTools.Components;
using Moka.DevTools.Patterns;
using Moka.DevTools.Text;

namespace Moka.DevTools.Tool;

/// <summary>
/// A JavaScript regex run against sample text in the page's worker: matches highlighted, each one listed with its
/// numbered and named groups, and what Mokaterm's terminal triggers would make of the pattern.
/// </summary>
public sealed partial class RegexSection : DevToolViewBase, IDisposable
{
	private const int MaxListed = 200;

	private const int QuoteChars = 160;

	private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);

	private static readonly IReadOnlyList<(char Flag, string Meaning)> Flags =
	[
		('g', "g, global: every match, not only the first"),
		('i', "i, ignore case"),
		('m', "m, multiline: ^ and $ match at line breaks"),
		('s', "s, dotAll: . matches line breaks too"),
		('u', "u, unicode: code points, and \\p{...} classes"),
		('v', "v, unicode sets: u plus set operations in classes"),
		('y', "y, sticky: match only where the last match ended"),
	];

	private readonly SortedSet<char> _flags = ['g'];
	private string _pattern = "";
	private TextValue _sample = TextValue.Empty;
	private string _ranText = "";
	private RegexRunResult? _result;
	private bool _running;
	private int _runs;
	private CancellationTokenSource? _waiting;

	[CascadingParameter]
	private DevToolsScript Script { get; set; } = default!;

	private string FlagText => string.Concat(_flags);

	/// <summary>The flags a terminal trigger would not use: it compiles with g, and i when its rule says so.</summary>
	private string FlagsTriggersIgnore => string.Join(", ", _flags.Where(flag => flag is not 'g' and not 'i'));

	private string Summary => _result switch
	{
		{ Status: RegexRunStatus.Ok, Total: var total } => total == 1 ? "1 match" : string.Create(CultureInfo.CurrentCulture, $"{total:N0} matches"),
		{ Status: RegexRunStatus.Timeout } => "took too long",
		{ Status: RegexRunStatus.Syntax } => "invalid pattern",
		_ => "JavaScript semantics",
	};

	public void Dispose()
	{
		Stop();
		_sample = TextValue.Empty;
		_ranText = "";
		_result = null;
	}

	private static string MatchText(RegexRunResult result)
	{
		string count = result.Truncated
			? string.Create(CultureInfo.CurrentCulture, $"More than {result.Total:N0} matches; the first {result.Total:N0} are shown")
			: result.Total == 1 ? "1 match" : string.Create(CultureInfo.CurrentCulture, $"{result.Total:N0} matches");
		return string.Create(CultureInfo.CurrentCulture, $"{count}, found in {result.Elapsed:0.#} ms.");
	}

	private static string Range(int start, int end) => start == end
		? string.Create(CultureInfo.InvariantCulture, $"at {start}, empty")
		: string.Create(CultureInfo.InvariantCulture, $"{start} to {end}");

	private string Quote(int start, int end)
	{
		string? text = RegexHighlight.Slice(_ranText, start, end);
		if (text is null)
		{
			return "(outside the text)";
		}

		return text.Length > QuoteChars
			? string.Create(CultureInfo.CurrentCulture, $"{text[..QuoteChars]}... ({text.Length:N0} chars)")
			: text;
	}

	private Task OnPatternChangedAsync(string? value)
	{
		_pattern = value ?? "";
		return ScheduleAsync();
	}

	private Task OnFlagChangedAsync(char flag, bool on)
	{
		if (on)
		{
			_flags.Add(flag);

			// u and v cannot be set together; the newer choice wins rather than leaving a pattern that never compiles.
			if (flag == 'u')
			{
				_flags.Remove('v');
			}
			else if (flag == 'v')
			{
				_flags.Remove('u');
			}
		}
		else
		{
			_flags.Remove(flag);
		}

		return ScheduleAsync();
	}

	private Task OnSampleChangedAsync(TextValue value)
	{
		_sample = value;
		return ScheduleAsync();
	}

	// Waits for typing to settle, then runs; a newer change replaces a run still waiting.
	private async Task ScheduleAsync()
	{
		Stop();
		int run = ++_runs;
		if (_pattern.Length == 0 || _sample.IsEmpty)
		{
			_result = null;
			_running = false;
			return;
		}

		CancellationTokenSource waiting = new();
		_waiting = waiting;
		_running = true;
		string pattern = _pattern;
		string flags = FlagText;
		string text = _sample.Text;
		RegexRunResult result;
		try
		{
			await Task.Delay(Settle, Time, waiting.Token);
			result = await Script.RunRegexAsync(
				new RegexRequest(pattern, flags, text, RegexRequest.DefaultTimeoutMs, RegexRequest.DefaultMaxMatches, RegexRequest.DefaultMaxGroups),
				waiting.Token);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (Exception ex) when (Mokaterm.UI.Common.Interop.JsModule.IsTeardown(ex))
		{
			return;
		}

		if (run != _runs || result.Status == RegexRunStatus.Superseded)
		{
			return;
		}

		_running = false;
		_ranText = text;
		_result = result;
	}

	private void Stop()
	{
		if (Interlocked.Exchange(ref _waiting, null) is { } previous)
		{
			previous.Cancel();
			previous.Dispose();
		}
	}
}
