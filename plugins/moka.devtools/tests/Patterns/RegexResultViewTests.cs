using Moka.DevTools.Components;
using Moka.DevTools.Patterns;
using Moka.DevTools.Tests.Shared;

namespace Moka.DevTools.Tests.Patterns;

public sealed class RegexResultViewTests
{
	private const string Sample = "Deployed 2024-05-06, rolled back 2024-05-07.";

	[Fact]
	public async Task Matches_AreMarkedAndListedWithAColumnPerGroup()
	{
		RegexMatchSpan first = new(9, 19, [new(1, "year", 9, 13), new(2, "month", 14, 16), new(3, null, -1, -1)]);
		RegexMatchSpan second = new(33, 43, [new(1, "year", 33, 37), new(2, "month", 38, 40), new(3, null, -1, -1)]);
		RegexRunResult result = new(RegexRunStatus.Ok, null, [first, second], 2, false, 0.4, new RegexTriggerCheck(false, null));

		string html = await RenderAsync(result);

		Assert.Contains("2 matches, found in 0.4 ms.", html, StringComparison.Ordinal);
		Assert.Equal(2, Render.Count(html, "<mark"));
		Assert.Contains(">year<", html, StringComparison.Ordinal);
		Assert.Contains(">month<", html, StringComparison.Ordinal);
		Assert.Contains(">Group 3<", html, StringComparison.Ordinal);
		Assert.Contains("Did not take part", html, StringComparison.Ordinal);
		Assert.Contains("9 to 19", html, StringComparison.Ordinal);
		Assert.Contains("2 listed, 3 groups", html, StringComparison.Ordinal);
		Assert.Contains("moka-mark--info", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ManyGroups_MoveIntoEachRowsDetail()
	{
		RegexGroupSpan[] groups = [.. Enumerable.Range(1, 8).Select(number => new RegexGroupSpan(number, $"g{number}", 9 + number, 10 + number))];
		RegexRunResult result = new(RegexRunStatus.Ok, null, [new RegexMatchSpan(9, 19, groups)], 1, false, 0.2, null);

		string html = await RenderAsync(result);

		Assert.DoesNotContain(">g1<", html, StringComparison.Ordinal);
		Assert.Contains("1 listed, 8 groups", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task EmptyMatch_IsAThinBar()
	{
		RegexRunResult result = new(RegexRunStatus.Ok, null, [new RegexMatchSpan(0, 0, null)], 1, false, 0.1, null);

		string html = await RenderAsync(result);

		Assert.Contains(HighlightedText.EmptyMatchClass, html, StringComparison.Ordinal);
		Assert.Contains("at 0, empty", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(RegexRunStatus.Timeout, "Took too long")]
	[InlineData(RegexRunStatus.Syntax, "Not a valid pattern")]
	[InlineData(RegexRunStatus.Unavailable, "Not run")]
	public async Task Failures_SayWhatHappened(string status, string title)
	{
		RegexRunResult result = new(status, "Something went wrong.", null, 0, false, 0, null);

		string html = await RenderAsync(result);

		Assert.Contains(title, html, StringComparison.Ordinal);
		Assert.DoesNotContain("<mark", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Triggers_ThatWouldRefuse_AreNamed()
	{
		RegexRunResult result = new(RegexRunStatus.Ok, null, [], 0, false, 0.1, new RegexTriggerCheck(true, "nested repetition"));

		string html = await RenderAsync(result);

		Assert.Contains("Terminal triggers would refuse this pattern", html, StringComparison.Ordinal);
		Assert.Contains("nested repetition", html, StringComparison.Ordinal);
		Assert.Contains("No match", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Triggers_ThatWouldTakeIt_SayWhichFlagsAreLost()
	{
		RegexRunResult result = new(RegexRunStatus.Ok, null, [], 0, false, 0.1, new RegexTriggerCheck(false, null));

		string html = await Render.HtmlAsync<RegexResultView>(new()
		{
			[nameof(RegexResultView.Result)] = result,
			[nameof(RegexResultView.Text)] = Sample,
			[nameof(RegexResultView.IgnoredFlags)] = "m, s",
		});

		Assert.Contains("m, s would not carry over", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(RegexRunResult result) =>
		Render.HtmlAsync<RegexResultView>(new()
		{
			[nameof(RegexResultView.Result)] = result,
			[nameof(RegexResultView.Text)] = Sample,
		});
}
