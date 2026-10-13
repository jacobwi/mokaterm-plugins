using System.Net;

namespace Moka.Network.Lookup;

/// <summary>
/// Looks up names for the addresses a trace meets, each address once, a few at a time, without holding the trace
/// up: a hop is drawn at once and its name fills in when the PTR answer arrives. Addresses without a name are simply
/// never reported.
/// </summary>
public sealed class ReverseNames
{
	/// <summary>Lookups in flight at once; the rest wait their turn in arrival order.</summary>
	public const int MaxConcurrent = 4;

	private readonly Func<IPAddress, CancellationToken, Task<string?>> _lookup;
	private readonly Action<IPAddress, string> _onName;
	private readonly CancellationToken _cancellationToken;
	private readonly HashSet<IPAddress> _asked = [];
	private readonly Queue<IPAddress> _waiting = new();
	private readonly Lock _lock = new();
	private int _active;

	public ReverseNames(Func<IPAddress, CancellationToken, Task<string?>> lookup, Action<IPAddress, string> onName, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(lookup);
		ArgumentNullException.ThrowIfNull(onName);
		_lookup = lookup;
		_onName = onName;
		_cancellationToken = cancellationToken;
	}

	/// <summary>How many distinct addresses were asked about.</summary>
	public int Asked
	{
		get
		{
			lock (_lock)
			{
				return _asked.Count;
			}
		}
	}

	public void Request(IEnumerable<IPAddress> addresses)
	{
		ArgumentNullException.ThrowIfNull(addresses);
		int start = 0;
		lock (_lock)
		{
			foreach (IPAddress address in addresses)
			{
				if (_asked.Add(address))
				{
					_waiting.Enqueue(address);
				}
			}

			start = Math.Min(MaxConcurrent - _active, _waiting.Count);
			_active += start;
		}

		for (int index = 0; index < start; index++)
		{
			_ = Task.Run(WorkAsync, CancellationToken.None);
		}
	}

	private async Task WorkAsync()
	{
		while (true)
		{
			IPAddress next;
			lock (_lock)
			{
				if (_waiting.Count == 0 || _cancellationToken.IsCancellationRequested)
				{
					_active--;
					return;
				}

				next = _waiting.Dequeue();
			}

			string? name;
			try
			{
				name = await _lookup(next, _cancellationToken);
			}
			catch (Exception)
			{
				// A name is a nicety beside the hop: whatever stopped this lookup must not stop the worker taking the next.
				name = null;
			}

			if (name is not null)
			{
				_onName(next, name);
			}
		}
	}
}
