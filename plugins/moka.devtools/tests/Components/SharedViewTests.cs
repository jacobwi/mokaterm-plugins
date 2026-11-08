using Microsoft.AspNetCore.Components;
using Moka.DevTools.Components;
using Moka.DevTools.Tests.Shared;
using Moka.DevTools.Text;

namespace Moka.DevTools.Tests.Components;

public sealed class SharedViewTests
{
	[Fact]
	public async Task CodeOutput_ShowsTheTextWithItsOwnCopyAndSave()
	{
		string html = await Render.HtmlAsync<CodeOutput>(new()
		{
			[nameof(CodeOutput.Title)] = "Output",
			[nameof(CodeOutput.Detail)] = "11 chars",
			[nameof(CodeOutput.Text)] = "hello world",
			[nameof(CodeOutput.FileName)] = "encoded.txt",
		});

		Assert.Contains("hello world", html, StringComparison.Ordinal);
		Assert.Contains("11 chars", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy Output\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Save as encoded.txt\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-code-block__copy", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task CodeOutput_KeepsASecretHiddenUntilShown()
	{
		string html = await Render.HtmlAsync<CodeOutput>(new()
		{
			[nameof(CodeOutput.Title)] = "Payload",
			[nameof(CodeOutput.Text)] = "the payload",
			[nameof(CodeOutput.Secret)] = true,
		});

		Assert.DoesNotContain("the payload", html, StringComparison.Ordinal);
		Assert.Contains("Hidden. Copy it, save it, or show it here.", html, StringComparison.Ordinal);
		Assert.Contains(">Show<", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy Payload\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task CodeOutput_DrawsOnlyTheStartOfAVeryLongText()
	{
		string text = new('x', TextLimits.DisplayChars + 10);

		string html = await Render.HtmlAsync<CodeOutput>(new()
		{
			[nameof(CodeOutput.Title)] = "Output",
			[nameof(CodeOutput.Text)] = text,
		});

		Assert.DoesNotContain(text, html, StringComparison.Ordinal);
		Assert.Contains("Copy and Save take all of it.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TextInput_HoldsTextInABoxWithItsCount()
	{
		string html = await Render.HtmlAsync<TextInput>(new()
		{
			[nameof(TextInput.Label)] = "Sample text",
			[nameof(TextInput.Value)] = new TextValue { Text = "abc" },
			[nameof(TextInput.AllowFile)] = true,
		});

		Assert.Contains("<textarea", html, StringComparison.Ordinal);
		Assert.Contains("3 chars", html, StringComparison.Ordinal);
		Assert.Contains("Open file", html, StringComparison.Ordinal);
		Assert.Contains("Clear", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TextInput_ShowsAFileItCannotHoldAsATag()
	{
		string html = await Render.HtmlAsync<TextInput>(new()
		{
			[nameof(TextInput.Label)] = "Text or a file",
			[nameof(TextInput.Value)] = new TextValue { Bytes = new byte[2048], FileName = "image.png" },
		});

		Assert.DoesNotContain("<textarea", html, StringComparison.Ordinal);
		Assert.Contains(">image.png<", html, StringComparison.Ordinal);
		Assert.Contains("2 KB, not text", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(ResultTone.Good, "moka-alert--success")]
	[InlineData(ResultTone.Bad, "moka-alert--error")]
	[InlineData(ResultTone.Warning, "moka-alert--warning")]
	[InlineData(ResultTone.Plain, "moka-alert--info")]
	public async Task ResultLine_IsAnAlertOfItsTone(ResultTone tone, string cssClass)
	{
		string html = await Render.HtmlAsync<ResultLine>(new()
		{
			[nameof(ResultLine.Title)] = "Matches",
			[nameof(ResultLine.Tone)] = tone,
			[nameof(ResultLine.ChildContent)] = (RenderFragment)(builder => builder.AddContent(0, "The hex hash is the SHA-256.")),
		});

		Assert.Contains(cssClass, html, StringComparison.Ordinal);
		Assert.Contains(">Matches<", html, StringComparison.Ordinal);
		Assert.Contains("The hex hash is the SHA-256.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task SectionFrame_PutsTheTitleAndSummaryInMokasHeader()
	{
		string html = await Render.HtmlAsync<SectionFrame>(new()
		{
			[nameof(SectionFrame.Title)] = "Hashes",
			[nameof(SectionFrame.Said)] = "Every hash at once",
		});

		Assert.Contains("moka-page-header", html, StringComparison.Ordinal);
		Assert.Contains(">Hashes<", html, StringComparison.Ordinal);
		Assert.Contains("Every hash at once", html, StringComparison.Ordinal);
	}
}
