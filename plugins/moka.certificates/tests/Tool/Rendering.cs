using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Plugin.TestHost;

namespace Moka.Certificates.Tests.Tool;

/// <summary>Renders a view at <see cref="TestMaterial.Now"/>, so days left and the validity gauge come out the same on every run.</summary>
internal static class Rendering
{
	public static Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters, HostKind host = HostKind.Desktop, Action<IServiceCollection>? configure = null)
		where TComponent : IComponent =>
		StaticRender.RenderAsync<TComponent>(parameters, host, services =>
		{
			services.AddSingleton<TimeProvider>(new FixedTime(TestMaterial.Now));
			configure?.Invoke(services);
		});

	/// <summary>A clock that always says the same time.</summary>
	private sealed class FixedTime(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}
}
