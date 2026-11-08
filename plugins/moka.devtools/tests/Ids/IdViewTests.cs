using Moka.DevTools.Components;
using Moka.DevTools.Ids;
using Moka.DevTools.Tests.Shared;

namespace Moka.DevTools.Tests.Ids;

public sealed class IdViewTests
{
	[Fact]
	public async Task Generated_ListsEachIdWithItsOwnCopy()
	{
		IReadOnlyList<string> ids = IdGenerator.Generate(IdKind.UuidV7, 3, upper: false, hyphens: true, Render.Now);

		string html = await Render.HtmlAsync<GeneratedIds>(new()
		{
			[nameof(GeneratedIds.Ids)] = ids,
			[nameof(GeneratedIds.Kind)] = "UUID v7",
		});

		Assert.All(ids, id => Assert.Contains(id, html, StringComparison.Ordinal));
		Assert.Contains("3 ids, UUID v7", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy id 2\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy them all, one a line\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Save as ids.txt\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Generated_PagesALongBatch()
	{
		IReadOnlyList<string> ids = IdGenerator.Generate(IdKind.Ulid, 60, upper: true, hyphens: false, Render.Now);

		string html = await Render.HtmlAsync<GeneratedIds>(new() { [nameof(GeneratedIds.Ids)] = ids });

		Assert.Contains(ids[0], html, StringComparison.Ordinal);
		Assert.DoesNotContain(ids[^1], html, StringComparison.Ordinal);
		Assert.Contains("60 ids", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Report_TakesAUuidApart()
	{
		IdReport report = IdInspector.Inspect("0192f3a4-5b6c-7d8e-9f01-23456789abcd");

		string html = await Render.HtmlAsync<IdReportView>(new() { [nameof(IdReportView.Report)] = report });

		Assert.Contains(">UUID<", html, StringComparison.Ordinal);
		Assert.Contains(report.Summary, html, StringComparison.Ordinal);
		Assert.Contains("0192f3a4-5b6c-7d8e-9f01-23456789abcd", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy the canonical form\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy the ULID form\"", html, StringComparison.Ordinal);
		Assert.Contains("2024-11-03T20:08:07.7880000Z", html, StringComparison.Ordinal);
		Assert.Contains("12 months ago", html, StringComparison.Ordinal);
	}
}
