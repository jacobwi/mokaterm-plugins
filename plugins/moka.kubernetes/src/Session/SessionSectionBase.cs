using Microsoft.AspNetCore.Components;
using Moka.Kubernetes.Server;
using Moka.Kubernetes.Settings;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;

namespace Moka.Kubernetes.Session;

/// <summary>
/// What every tab of the session tool shares: the session it asks through, the settings, and the one run a tab can
/// have at a time. A run's work goes off the render thread and comes back through <see cref="Post"/>; closing the
/// tool cancels it. The session handle's <see cref="ISessionHandle.Changed"/> is heard here, so a tab that reconnects
/// can ask the server again and a disconnect is drawn as what it is.
/// </summary>
public abstract class SessionSectionBase : ComponentBase, IDisposable
{
	private CancellationTokenSource? _run;
	private bool _disposed;

	[Inject]
	protected ISettingsService SettingsService { get; set; } = default!;

	/// <summary>The session this tab asks through, given by the tool. Null in a bare render, which every tab survives.</summary>
	[Parameter]
	public ISessionHandle? Session { get; set; }

	/// <summary>True while this tab is the one on show. A hidden tab keeps its state, but stops redrawing.</summary>
	[Parameter]
	public bool IsActive { get; set; }

	protected KubernetesSettings Settings => SettingsService.Get<KubernetesSettings>().Clamped();

	/// <summary>True while the session is connected, which is the only time anything can run on the server.</summary>
	protected bool Connected => Session is { State: SessionState.Connected };

	/// <summary>A runner on the live session, or null with the state already saying why: only a connected session has a feature.</summary>
	protected ServerRunner? Runner => Connected && Session is { } session ? ServerRunner.For(session) : null;

	/// <summary>A run is going and has not been told to stop.</summary>
	protected bool Running => _run is { IsCancellationRequested: false };

	/// <summary>A run was started and has not finished yet, stopping or not.</summary>
	protected bool Busy => _run is not null;

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
		if (Session is { } session)
		{
			session.Changed -= SessionChanged;
		}

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

	protected override void OnInitialized()
	{
		if (Session is { } session)
		{
			session.Changed += SessionChanged;
		}
	}

	/// <summary>The session connected, came apart or reconnected: a tab asks again when it lands connected.</summary>
	protected virtual void OnSessionChange()
	{
	}

	/// <summary>A count as the tabs print it: grouped thousands, in the user's own digits.</summary>
	protected static string Count(long value) => value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

	/// <summary>True once the tool closed, which every background callback checks before it touches the tab again.</summary>
	protected bool Disposed => _disposed;

	private void SessionChanged()
	{
		// The handle says handlers may run on any thread; the tab reads it back on the renderer.
		_ = InvokeAsync(() =>
		{
			if (_disposed)
			{
				return;
			}

			OnSessionChange();
			StateHasChanged();
		});
	}
}
