using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Platform;

namespace Moka.DevTools.Tests.Rendering;

/// <summary>
/// Renders a component to HTML with Blazor's static renderer and the services the plugin's views inject, faked. The
/// browser never runs, so after-render interop and event handlers stay out of reach.
/// </summary>
internal static class StaticRender
{
	public static async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters, HostKind host = HostKind.Desktop)
		where TComponent : IComponent
	{
		ServiceCollection services = new();
		services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
		services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
		services.AddScoped<IJSRuntime, IdleJsRuntime>();
		services.AddSingleton<IClipboardService, FakeClipboard>();
		services.AddSingleton<IUserInteraction, FakeInteraction>();
		services.AddSingleton<ILocalFileAccess, FakeFiles>();
		services.AddSingleton<ISettingsService, FakeSettings>();
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton<IAppEnvironment>(new FakeEnvironment(host));
		await using ServiceProvider provider = services.BuildServiceProvider();
		await using AsyncServiceScope scope = provider.CreateAsyncScope();
		await using HtmlRenderer renderer = new(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());

		using CancellationTokenSource limit = new(TimeSpan.FromSeconds(30));
		return await renderer.Dispatcher.InvokeAsync(async () =>
		{
			HtmlRootComponent component = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
			return component.ToHtmlString();
		}).WaitAsync(limit.Token);
	}

	/// <summary>Like the runtime Blazor uses while prerendering, every call throws, which components read as "no JavaScript yet".</summary>
	private sealed class IdleJsRuntime : IJSRuntime
	{
		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw NoJavaScript(identifier);

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw NoJavaScript(identifier);

		private static InvalidOperationException NoJavaScript(string identifier) =>
			new($"JavaScript interop calls cannot be issued during static rendering ({identifier}).");
	}

	private sealed class FakeClipboard : IClipboardService
	{
		public ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

		public ValueTask<string?> ReadTextAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);

		public ValueTask WriteSecretAsync(string secret, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
	}

	private sealed class FakeInteraction : IUserInteraction
	{
		public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public void Notify(NoticeSeverity severity, string message, string? title = null)
		{
		}
	}

	private sealed class FakeFiles : ILocalFileAccess
	{
		public bool CanPickFolders => false;

		public ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<IReadOnlyList<LocalFileItem>>([]);

		public ValueTask<IReadOnlyList<LocalFileItem>> PickFolderAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<IReadOnlyList<LocalFileItem>>([]);

		public ValueTask<bool> SaveFileAsync(string suggestedName, long? length, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(false);
	}

	private sealed class FakeSettings : ISettingsService
	{
		public event Action<string>? Changed
		{
			add { }
			remove { }
		}

		public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public T Get<T>()
			where T : class, ISettingsSection, new() => new();

		public Task UpdateAsync<T>(Func<T, T> update, CancellationToken cancellationToken = default)
			where T : class, ISettingsSection, new() => Task.CompletedTask;

		public Task ResetAsync<T>(CancellationToken cancellationToken = default)
			where T : class, ISettingsSection, new() => Task.CompletedTask;

		public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private sealed class FakeEnvironment(HostKind kind) : IAppEnvironment
	{
		public HostKind Kind => kind;

		public string PlatformName => "Test";

		public string AppVersion => "0.2.0";

		public string DataDirectory => Path.GetTempPath();

		public string TempDirectory => Path.GetTempPath();
	}
}
