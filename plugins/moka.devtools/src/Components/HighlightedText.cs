using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Moka.DevTools.Patterns;

namespace Moka.DevTools.Components;

/// <summary>
/// The sample with its matches marked, written as a render tree rather than markup because inside a <c>pre</c> every
/// character counts, and markup would bring its own indentation and line breaks.
/// </summary>
public sealed class HighlightedText : ComponentBase
{
	[Parameter, EditorRequired]
	public IReadOnlyList<HighlightSegment> Segments { get; set; } = [];

	[Parameter]
	public string? Class { get; set; }

	protected override void BuildRenderTree(RenderTreeBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.OpenElement(0, "pre");
		builder.AddAttribute(1, "class", Class);
		foreach (HighlightSegment segment in Segments)
		{
			if (!segment.IsMatch)
			{
				builder.AddContent(2, segment.Text);
				continue;
			}

			builder.OpenElement(3, "mark");
			builder.AddAttribute(4, "class", segment.Text.Length == 0
				? "dt-mark dt-mark--empty"
				: segment.Match % 2 == 0 ? "dt-mark" : "dt-mark dt-mark--odd");
			builder.AddAttribute(5, "title", string.Create(CultureInfo.CurrentCulture, $"Match {segment.Match + 1}"));
			builder.AddContent(6, segment.Text);
			builder.CloseElement();
		}

		builder.CloseElement();
	}
}
