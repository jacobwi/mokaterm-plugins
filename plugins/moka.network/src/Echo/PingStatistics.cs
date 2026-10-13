namespace Moka.Network.Echo;

/// <summary>
/// The summary under a ping run: sent, received, loss, round trip minimum, average and maximum, and jitter as the mean
/// difference between consecutive replies' round trips (RFC 3550's idea without its smoothing). Only an echo reply
/// counts as received; an expired TTL or an unreachable answer is a lost request, as ping itself counts it.
/// </summary>
public sealed class PingStatistics
{
	private double _sum;
	private double _jitterSum;
	private int _jitterCount;
	private double? _previous;

	public int Sent { get; private set; }

	public int Received { get; private set; }

	public int Lost => Sent - Received;

	/// <summary>Lost requests out of every hundred sent; 0 before anything was sent.</summary>
	public double LossPercent => Sent == 0 ? 0 : 100.0 * Lost / Sent;

	public double? Minimum { get; private set; }

	public double? Maximum { get; private set; }

	public double? Average => Received == 0 ? null : _sum / Received;

	/// <summary>Null until two replies have arrived, since one round trip has nothing to differ from.</summary>
	public double? Jitter => _jitterCount == 0 ? null : _jitterSum / _jitterCount;

	public void Add(EchoReply reply)
	{
		ArgumentNullException.ThrowIfNull(reply);
		Sent++;
		if (reply.Status != EchoStatus.Reply || reply.Milliseconds is not { } milliseconds)
		{
			return;
		}

		Received++;
		_sum += milliseconds;
		Minimum = Minimum is { } minimum ? Math.Min(minimum, milliseconds) : milliseconds;
		Maximum = Maximum is { } maximum ? Math.Max(maximum, milliseconds) : milliseconds;
		if (_previous is { } previous)
		{
			_jitterSum += Math.Abs(milliseconds - previous);
			_jitterCount++;
		}

		_previous = milliseconds;
	}
}
