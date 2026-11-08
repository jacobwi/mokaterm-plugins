using Moka.DevTools.Components;
using Moka.DevTools.Data;
using Moka.DevTools.Tests.Shared;
using Moka.DevTools.Tool;

namespace Moka.DevTools.Tests.Data;

public sealed class DataViewTests
{
	[Fact]
	public async Task Output_IsACodeBlockWithItsLanguageAndActions()
	{
		DataResult result = new()
		{
			Text = JsonTools.Format("""{"b":[1,2],"a":"x"}""", JsonIndent.TwoSpaces, sortKeys: false),
			Title = "Formatted JSON",
			FileName = "formatted.json",
		};

		string html = await Render.HtmlAsync<DataResultView>(new()
		{
			[nameof(DataResultView.Result)] = result,
			[nameof(DataResultView.UseAsInput)] = Microsoft.AspNetCore.Components.EventCallback.Factory.Create<string>(new object(), _ => { }),
		});

		Assert.Contains("Formatted JSON", html, StringComparison.Ordinal);
		Assert.Contains("7 lines", html, StringComparison.Ordinal);
		Assert.Contains("moka-code-block", html, StringComparison.Ordinal);
		Assert.Contains(">json<", html, StringComparison.Ordinal);
		Assert.Contains("\"a\": \"x\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy Formatted JSON\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Save as formatted.json\"", html, StringComparison.Ordinal);
		Assert.Contains("Use as input", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-code-block__copy", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Failure_IsAnAlertWithItsTitle()
	{
		string html = await Render.HtmlAsync<DataResultView>(new()
		{
			[nameof(DataResultView.Result)] = DataResult.Failed("Not valid", "Expected a value (line 1, column 2)"),
		});

		Assert.Contains("Not valid", html, StringComparison.Ordinal);
		Assert.Contains("Expected a value (line 1, column 2)", html, StringComparison.Ordinal);
		Assert.Contains("moka-alert", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Use as input", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Diff_DrawsItsHunksUnderTheirHeaders()
	{
		DiffResult diff = LineDiff.Compare("a\nb\nc\nd\ne\nf\ng\nh\ni", "a\nb\nc\nd\nE\nf\ng\nh\ni", TestContext.Current.CancellationToken);

		string html = await Render.HtmlAsync<DiffView>(new() { [nameof(DiffView.Diff)] = diff });

		Assert.Contains("@@ -2,7 +2,7 @@", html, StringComparison.Ordinal);
		Assert.Contains("1 unchanged line", html, StringComparison.Ordinal);
		Assert.Contains(">-1<", html, StringComparison.Ordinal);
		Assert.Contains(">+1<", html, StringComparison.Ordinal);
		Assert.Contains("moka-diff-viewer-line--removed", html, StringComparison.Ordinal);
		Assert.Contains("moka-diff-viewer-line--added", html, StringComparison.Ordinal);
		Assert.Contains("1 change", html, StringComparison.Ordinal);
	}
}
