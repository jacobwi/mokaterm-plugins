using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Plugin.TestHost;

namespace Moka.DevTools.Tests.Shared;

/// <summary>Renders a view at a fixed moment and hands back its HTML with the entities decoded, ready to search.</summary>
internal static class Render
{
	/// <summary>The clock every view test reads: 2025-11-08 12:00:00 UTC.</summary>
	public static readonly DateTimeOffset Now = new(2025, 11, 8, 12, 0, 0, TimeSpan.Zero);

	public static async Task<string> HtmlAsync<TComponent>(Dictionary<string, object?> parameters)
		where TComponent : IComponent
	{
		string html = await StaticRender.RenderAsync<TComponent>(parameters, HostKind.Desktop, services =>
		{
			services.AddSingleton<TimeProvider>(new FixedClock(Now));
			TestNavigation.Add(services);
		});
		return WebUtility.HtmlDecode(html);
	}

	public static int Count(string html, string text)
	{
		int count = 0;
		for (int at = html.IndexOf(text, StringComparison.Ordinal); at >= 0; at = html.IndexOf(text, at + text.Length, StringComparison.Ordinal))
		{
			count++;
		}

		return count;
	}

	private sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}
}
