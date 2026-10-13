using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Network.Settings;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Platform;

namespace Moka.Network.Components;

/// <summary>
/// What every tab of the tool shares: the settings, where probes come from, and the one run a tab can have at a time.
/// A run's work goes off the render thread and comes back through <see cref="Post"/>; closing the tool cancels it.
/// </summary>
public abstract class NetworkViewBase : ComponentBase, IDisposable
{
	private CancellationTokenSource? _run;
	private bool _disposed;

	[Inject]
	protected IAppEnvironment Environment { get; set; } = default!;

	[Inject]
	protected ISettingsService SettingsService { get; set; } = default!;

	[Inject]
	protected IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	protected IClipboardService Clipboard { get; set; } = default!;

	[Inject]
	protected TimeProvider Time { get; set; } = default!;

	protected NetworkSettings Settings => SettingsService.Get<NetworkSettings>().Clamped();

	/// <summary>A run is going and has not been told to stop.</summary>
	protected bool Running => _run is { IsCancellationRequested: false };

	/// <summary>A run was started and has not finished yet, stopping or not.</summary>
	protected bool Busy => _run is not null;

	/// <summary>"this computer" on the desktop, "the Mokaterm server" on the web host: where a probe really starts.</summary>
	protected string Origin => Environment.Kind == HostKind.Desktop ? "this computer" : "the Mokaterm server";

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		// Only cancelled here: the run's own finally disposes the source once its work has let go of the token.
		_run?.Cancel();
	}

	/// <summary>Starts a run, stopping one still going. Pair with <see cref="EndRun"/> in a finally.</summary>
	protected CancellationTokenSource BeginRun()
	{
		_run?.Cancel();
		CancellationTokenSource run = new();
		_run = run;
		return run;
	}

	protected void EndRun(CancellationTokenSource run)
	{
		ArgumentNullException.ThrowIfNull(run);
		if (ReferenceEquals(_run, run))
		{
			_run = null;
		}

		run.Dispose();
	}

	protected void Stop() => _run?.Cancel();

	/// <summary>Applies a change that a background run produced on the renderer, then draws it. Nothing happens once the tool closed.</summary>
	protected void Post(Action change)
	{
		ArgumentNullException.ThrowIfNull(change);
		_ = InvokeAsync(() =>
		{
			if (_disposed)
			{
				return;
			}

			change();
			StateHasChanged();
		});
	}

	protected async Task CopyAsync(string text, string what)
	{
		try
		{
			await Clipboard.WriteTextAsync(text);
			Interaction.Notify(NoticeSeverity.Success, $"{what} copied.");
		}
		catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.JSInterop.JSException or Microsoft.JSInterop.JSDisconnectedException)
		{
			Interaction.Notify(NoticeSeverity.Error, $"{what} could not be copied.");
		}
	}

	/// <summary>A round trip as the views print it: one decimal under 10 ms, whole milliseconds above.</summary>
	protected static string Milliseconds(double milliseconds) => milliseconds < 10
		? milliseconds.ToString("0.0", CultureInfo.CurrentCulture) + " ms"
		: milliseconds.ToString("N0", CultureInfo.CurrentCulture) + " ms";

	protected static string Milliseconds(TimeSpan elapsed) => Milliseconds(elapsed.TotalMilliseconds);

	protected static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

	/// <summary>A whole number typed into a small field, or null when it does not read or falls outside the range.</summary>
	protected static int? ReadNumber(string text, int min, int max) =>
		int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out int value) && value >= min && value <= max ? value : null;
}
