namespace Moka.Network.Targets;

/// <summary>
/// The hosts this app run has looked up or probed, newest first, so the next box need not retype them. Held in memory
/// only: nothing is written to disk or settings, the list dies with the app, and no size is kept beyond
/// <see cref="Limit"/>. Every tab of the tool shares the one list.
/// </summary>
public sealed class RecentTargets
{
	/// <summary>The most targets kept; older ones fall off the end.</summary>
	public const int Limit = 10;

	private readonly object _lock = new();
	private readonly List<string> _targets = [];

	/// <summary>
	/// Notes a target: moved to the front when it is already there, dropped silently when empty. A run that starts with
	/// a valid target notes it, whatever the probe then finds.
	/// </summary>
	public void Remember(string? target)
	{
		string name = (target ?? "").Trim();
		if (name.Length == 0)
		{
			return;
		}

		lock (_lock)
		{
			_targets.RemoveAll(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase));
			_targets.Insert(0, name);
			if (_targets.Count > Limit)
			{
				_targets.RemoveRange(Limit, _targets.Count - Limit);
			}
		}
	}

	/// <summary>The newest targets first, at most <paramref name="count"/> of them.</summary>
	public IReadOnlyList<string> Recent(int count)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
		lock (_lock)
		{
			return [.. _targets.Take(count)];
		}
	}
}
