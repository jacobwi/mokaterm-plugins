using System.Globalization;
using System.Net.NetworkInformation;
using Microsoft.AspNetCore.Components;

namespace Moka.Network.Tool;

/// <summary>
/// This machine's connection table, refreshed while the panel is on screen: the TCP connections it holds and the
/// ports it listens on, state names in words, IPv6 ends in brackets. It says plainly that no owning process can be
/// named, because the .NET table it reads does not carry one. Delivered unwired: the app tool places this panel on a
/// tab of its own.
/// </summary>
public sealed partial class DesktopConnectionsPanel : ComponentBase, IDisposable
{
	/// <summary>How often the table is read again while the panel is alive.</summary>
	public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(2);

	private System.Threading.Timer? _refresh;
	private bool _disposed;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	private IReadOnlyList<LocalConnectionRow> _connections = [];
	private IReadOnlyList<LocalListenerRow> _listeners = [];

	private string Read => $"Read {Time.GetLocalNow().ToString("T", CultureInfo.CurrentCulture)}";

	protected override void OnAfterRender(bool firstRender)
	{
		if (!firstRender)
		{
			return;
		}

		ReadTable();
		_refresh = new System.Threading.Timer(_ => ReadTable(), null, RefreshEvery, RefreshEvery);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_refresh?.Dispose();
		GC.SuppressFinalize(this);
	}

	/// <summary>Reads the table off the render thread and brings the rows back to it, which drops a refresh rather than stacking one.</summary>
	private void ReadTable()
	{
		_ = Task.Run(() =>
		{
			LocalTable table;
			try
			{
				table = LocalConnections.Collect();
			}
			catch (NetworkInformationException)
			{
				// The table read failed once; the next tick tries again.
				return;
			}

			_ = InvokeAsync(() =>
			{
				if (_disposed)
				{
					return;
				}

				// A read that says the same thing draws nothing: a repaint every two seconds of unchanged rows is
				// what smeared them in the WebView until a scroll cleared it.
				if (_connections.SequenceEqual(table.Connections) && _listeners.SequenceEqual(table.Listeners))
				{
					return;
				}

				_connections = table.Connections;
				_listeners = table.Listeners;
				StateHasChanged();
			});
		});
	}
}
