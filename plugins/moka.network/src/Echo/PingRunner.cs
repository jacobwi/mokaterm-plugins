namespace Moka.Network.Echo;

/// <summary>
/// Sends a ping run's requests one after another, the next one an interval after the previous one started, and hands
/// each outcome over as it arrives. Stops after the count, or when cancelled for a continuous run.
/// </summary>
public static class PingRunner
{
	public static async Task RunAsync(IPinger pinger, PingPlan plan, Action<int, EchoReply> onReply, TimeProvider time, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pinger);
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(onReply);
		ArgumentNullException.ThrowIfNull(time);
		TimeSpan interval = plan.Interval < PingPlan.MinInterval ? PingPlan.MinInterval : plan.Interval;
		for (int sequence = 1; plan.Continuous || sequence <= plan.Count; sequence++)
		{
			long started = time.GetTimestamp();
			EchoReply reply = await pinger.SendAsync(plan.Address, plan.Request, cancellationToken);
			onReply(sequence, reply);
			if (!plan.Continuous && sequence == plan.Count)
			{
				return;
			}

			TimeSpan wait = interval - time.GetElapsedTime(started);
			if (wait > TimeSpan.Zero)
			{
				await Task.Delay(wait, time, cancellationToken);
			}
		}
	}
}
