using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Moka.DevTools.Patterns;
using Moka.Red.Core.Enums;
using Moka.Red.Primitives.Typography;

namespace Moka.DevTools.Components;

/// <summary>
/// The sample with its matches marked by Moka's mark, written as a render tree rather than markup because inside a
/// <c>pre</c> every character counts, and markup would bring its own indentation and line breaks. Neighbouring matches
/// take turns between two colours, so where one ends and the next starts stays visible.
/// </summary>
public sealed class HighlightedText : ComponentBase
{
	/// <summary>The class an empty match's mark carries, which has no text to colour and is drawn as a thin bar.</summary>
	public const string EmptyMatchClass = "dt-mark-empty";

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

			string text = segment.Text;
			builder.OpenComponent<MokaMark>(3);
			builder.AddComponentParameter(4, nameof(MokaMark.Color), segment.Match % 2 == 0 ? MokaColor.Primary : MokaColor.Info);
			builder.AddComponentParameter(5, nameof(MokaMark.Class), text.Length == 0 ? EmptyMatchClass : null);
			builder.AddComponentParameter(6, nameof(MokaMark.ChildContent), (RenderFragment)(inner => inner.AddContent(0, text)));
			builder.CloseComponent();
		}

		builder.CloseElement();
	}
}
