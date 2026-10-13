using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Moka.DevTools.Patterns;
using Moka.DevTools.Text;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.UI.Common.Interop;

namespace Moka.DevTools.Components;

/// <summary>
/// The tool's one handle on its page script: the paste guard on the tool's root and the regex worker. The tool creates
/// it and cascades it to the sections, and releases it when the tool closes, which also stops the worker.
/// </summary>
public sealed class DevToolsScript : IAsyncDisposable
{
	// The same path a built-in module uses: the host serves a plugin's wwwroot under its assembly name.
	private const string ScriptPath = "./_content/Moka.DevTools/js/devtools.js";

	private static readonly TimeSpan AttachWait = TimeSpan.FromSeconds(10);

	private readonly JsModule _module;
	private readonly IUserInteraction _interaction;
	private readonly TaskCompletionSource<int?> _attached = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private DotNetObjectReference<DevToolsScript>? _self;
	private bool _disposed;

	public DevToolsScript(IJSRuntime js, IUserInteraction interaction)
	{
		_module = new JsModule(js, ScriptPath);
		_interaction = interaction;
	}

	/// <summary>Watches pastes in <paramref name="root"/>; on the web host a paste is also held to what one message may carry.</summary>
	public async Task AttachAsync(ElementReference root, bool web)
	{
		_self = DotNetObjectReference.Create(this);
		try
		{
			int id = await _module.InvokeAsync<int>("attach", root, _self, new
			{
				maxChars = TextLimits.MaxTextChars,
				maxBytes = web ? TextLimits.WebPasteBytes : 0,
			});

			// The tool may have closed while the script loaded; its instance then has no one to release it.
			if (!_attached.TrySetResult(id))
			{
				await _module.TryInvokeVoidAsync("release", id);
			}
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			// Without the script the tool still works; only the paste guard and the regex tester are gone.
			_attached.TrySetResult(null);
		}
	}

	/// <summary>Runs a pattern in the page's worker, which the page stops after <see cref="RegexRequest.TimeoutMs"/>.</summary>
	public async Task<RegexRunResult> RunRegexAsync(RegexRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		int? id;
		try
		{
			id = await _attached.Task.WaitAsync(AttachWait, cancellationToken);
		}
		catch (TimeoutException)
		{
			id = null;
		}

		if (id is not { } instance || _disposed)
		{
			return RegexRunResult.Unavailable("The tester's script did not load in this page, so nothing ran.");
		}

		try
		{
			return await _module.InvokeAsync<RegexRunResult>("runRegex", cancellationToken, instance, request);
		}
		catch (JSException)
		{
			return RegexRunResult.Unavailable("The tester's script failed in this page, so nothing ran.");
		}
	}

	/// <summary>The page stopped a paste that would have made a box larger than the tool takes.</summary>
	[JSInvokable]
	public void OnPasteRefused(long characters, string reason)
	{
		string size = characters.ToString("N0", CultureInfo.CurrentCulture);
		_interaction.Notify(NoticeSeverity.Warning, reason == "bytes"
			? $"That paste would make the box {size} characters, more than the web version can send at once. Open it as a file instead."
			: string.Create(CultureInfo.CurrentCulture, $"That paste would make the box {size} characters; the tool takes at most {TextLimits.MaxTextChars / (1024 * 1024)} MiB of text."));
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_attached.TrySetResult(null);
		if (_attached.Task.Result is { } id)
		{
			await _module.TryInvokeVoidAsync("release", id);
		}

		await _module.DisposeAsync();
		_self?.Dispose();
	}
}
