using System.Globalization;
using System.Text;
using Moka.Kubernetes.Server;

namespace Moka.Kubernetes.Tests.Server;

/// <summary>One pod's JSON record, read as far as it goes and no further.</summary>
public sealed class PodDetailTests
{
	[Fact]
	public void Parse_ReadsThePodWhole()
	{
		PodDetail detail = PodDetail.Parse(Fixtures.PodJson)!;

		Assert.NotNull(detail);
		Assert.Equal("web-6799fc88d1-kx4t2", detail.Name);
		Assert.Equal("default", detail.Namespace);
		Assert.Equal("Running", detail.Phase);
		Assert.Equal("node-a.example", detail.Node);
		Assert.Equal("10.244.0.15", detail.Ip);
		Assert.Equal("Burstable", detail.QosClass);
		Assert.Equal("2026-10-27T08:14:02Z", detail.StartTime);
		Assert.Equal("Always", detail.RestartPolicy);
	}

	[Fact]
	public void Parse_TheContainersComeWithTheirStates()
	{
		PodDetail detail = PodDetail.Parse(Fixtures.PodJson)!;

		Assert.Equal(2, detail.Containers.Count);
		Assert.Equal(["app", "metrics"], detail.ContainerNames);

		PodContainer crashing = detail.Containers[0];
		Assert.Equal("registry.example/web:1.4.2", crashing.Image);
		Assert.False(crashing.Ready);
		Assert.Equal(5, crashing.Restarts);
		Assert.Equal(PodContainerState.Waiting, crashing.State);
		Assert.Equal("CrashLoopBackOff: back-off 5m0s restarting failed container", crashing.Detail);

		PodContainer running = detail.Containers[1];
		Assert.True(running.Ready);
		Assert.Equal(PodContainerState.Running, running.State);
		Assert.Equal("since 2026-10-27T08:14:04Z", running.Detail);
		Assert.Equal("running", running.StateWord);
	}

	[Fact]
	public void Parse_TheConditionsKeepTheirReasons()
	{
		PodDetail detail = PodDetail.Parse(Fixtures.PodJson)!;

		Assert.Equal(2, detail.Conditions.Count);
		Assert.Equal("PodScheduled", detail.Conditions[0].Type);
		Assert.Equal("True", detail.Conditions[0].Status);

		Assert.Equal("Ready", detail.Conditions[1].Type);
		Assert.Equal("False", detail.Conditions[1].Status);
		Assert.Equal("ContainersNotReady", detail.Conditions[1].Reason);
		Assert.Contains("unready", detail.Conditions[1].Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_APodStillBeingScheduled_HasNoNodeAndNoStatusesAndSaysSo()
	{
		PodDetail detail = PodDetail.Parse(Fixtures.PodJsonPending)!;

		Assert.NotNull(detail);
		Assert.Equal("Pending", detail.Phase);
		Assert.Equal("", detail.Node);
		Assert.Equal("", detail.Ip);
		Assert.Equal("", detail.StartTime);

		// The spec's containers are there even though none has a status yet.
		Assert.Equal(2, detail.Containers.Count);
		Assert.All(detail.Containers, container =>
		{
			Assert.Equal(PodContainerState.Unknown, container.State);
			Assert.False(container.Ready);
			Assert.Equal(0, container.Restarts);
		});

		PodCondition unschedulable = Assert.Single(detail.Conditions);
		Assert.Equal("Unschedulable", unschedulable.Reason);
	}

	[Fact]
	public void Parse_ATerminatedContainer_NamesTheReasonAndTheExit()
	{
		string json = """
			{
			  "metadata": { "name": "job", "namespace": "default" },
			  "spec": { "containers": [ { "name": "step", "image": "busybox" } ] },
			  "status": {
			    "phase": "Succeeded",
			    "containerStatuses": [
			      { "name": "step", "ready": false, "restartCount": 0,
			        "state": { "terminated": { "reason": "Completed", "exitCode": 0, "finishedAt": "2026-11-01T09:00:05Z" } } }
			    ]
			  }
			}
			""";

		PodContainer container = PodDetail.Parse(json)!.Containers.Single();

		Assert.Equal(PodContainerState.Terminated, container.State);
		Assert.Equal("Completed (exit 0)", container.Detail);
		Assert.Equal("terminated", container.StateWord);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("not json")]
	[InlineData("[1, 2, 3]")]
	[InlineData("{ \"nothing\": \"of a pod\" }")]
	public void Parse_OfAnythingElse_ReadsAsNothing(string json)
	{
		Assert.Null(PodDetail.Parse(json));
	}

	[Fact]
	public void Parse_OfJsonTooDeepToBeAPod_Survives()
	{
		// A hostile or truncated answer must never crash the panel; it reads as nothing.
		Assert.Null(PodDetail.Parse(new string('{', 20)));
	}
}

/// <summary>Log lines with timestamps, the follow poll's since-time, and the ring that keeps them.</summary>
public sealed class PodLogsTests
{
	[Fact]
	public void ReadLine_SplitsTheTimestampFromTheTextAtNineDigits()
	{
		PodLogLine line = PodLogs.ReadLine("2026-11-01T09:00:00.123456789Z starting worker version 1.4.2");

		Assert.NotNull(line.Time);
		Assert.Equal(DateTimeOffset.Parse("2026-11-01T09:00:00.1234567Z", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind), line.Time);
		Assert.Equal("starting worker version 1.4.2", line.Text);
		Assert.Equal("2026-11-01T09:00:00.123456789Z starting worker version 1.4.2", line.Raw);
	}

	[Fact]
	public void ReadLine_WholeSecondsAndNoFractionAreTimestampsToo()
	{
		PodLogLine whole = PodLogs.ReadLine("2026-11-01T09:00:02Z job 1 done");
		PodLogLine offset = PodLogs.ReadLine("2026-11-01T10:00:02+01:00 shifted");

		Assert.NotNull(whole.Time);
		Assert.Equal("job 1 done", whole.Text);
		Assert.NotNull(offset.Time);
		Assert.Equal(TimeSpan.FromHours(1), offset.Time.Value.Offset);
	}

	[Fact]
	public void ReadLine_WithoutATimestamp_KeepsTheWholeLine()
	{
		PodLogLine line = PodLogs.ReadLine(Fixtures.LogLineWithoutTime);

		Assert.Null(line.Time);
		Assert.Equal(Fixtures.LogLineWithoutTime, line.Text);
		Assert.Equal(Fixtures.LogLineWithoutTime, line.Raw);
	}

	[Fact]
	public void SinceTime_IsTheNextSecondFloored()
	{
		// The newest line's second plus one, floored: always past the newest line, never more than a second past it.
		Assert.Equal("2026-11-01T09:00:01Z", PodLogs.SinceTime(DateTimeOffset.Parse("2026-11-01T09:00:00.123456789Z", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind)));
		Assert.Equal("2026-11-01T09:00:01Z", PodLogs.SinceTime(DateTimeOffset.Parse("2026-11-01T09:00:00.900000000Z", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind)));
		Assert.Equal("2026-11-01T09:00:02Z", PodLogs.SinceTime(DateTimeOffset.Parse("2026-11-01T09:00:01.000000001Z", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind)));
	}

	[Fact]
	public void SinceTime_AnOffsetTimestamp_BecomesUtc()
	{
		Assert.Equal("2026-11-01T09:00:01Z", PodLogs.SinceTime(DateTimeOffset.Parse("2026-11-01T10:00:00.5+01:00", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind)));
	}

	[Fact]
	public void LogLines_AppendsTheBlockAndCountsTheLines()
	{
		LogLines lines = new();
		lines.Append(Fixtures.LogTail);

		Assert.Equal(3, lines.Count);
		Assert.Equal(3, lines.Total);
		Assert.Equal(3, lines.Snapshot().Count);
		Assert.Contains(lines.Snapshot(), line => line.Text == "job 1 done in 1.7 s");
		Assert.Equal(Fixtures.LogTail.Split('\n')[2], lines.Snapshot()[2].Raw);
	}

	[Fact]
	public void LogLines_ALineBroughtAgainByAPoll_IsNotListedTwice()
	{
		LogLines lines = new();
		lines.Append(Fixtures.LogTail);
		lines.Append($"{Fixtures.LogTail}\n2026-11-01T09:00:03Z a new line\n");

		Assert.Equal(4, lines.Count);
		Assert.Equal(3, lines.Skipped);
		Assert.Equal(0, lines.Dropped);
		Assert.Contains(lines.Snapshot(), line => line.Text == "a new line");
	}

	[Fact]
	public void LogLines_NewestIsTheLastKeptLine()
	{
		LogLines lines = new();
		Assert.Null(lines.Newest());
		lines.Append(Fixtures.LogTail);

		DateTimeOffset newest = Assert.NotNull(lines.Newest());
		Assert.Equal(DateTimeOffset.Parse("2026-11-01T09:00:02.000000001Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), newest);
	}

	[Fact]
	public void LogLines_TheRingDropsTheOldestAndCountsThem()
	{
		LogLines lines = new();
		StringBuilder block = new();
		for (int index = 0; index < LogLines.Capacity + 10; index++)
		{
			block.Append("2026-11-01T09:00:00Z line ").Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
		}

		lines.Append(block.ToString());

		Assert.Equal(LogLines.Capacity, lines.Count);
		Assert.Equal(10, lines.Dropped);
		Assert.Equal(LogLines.Capacity + 10, lines.Total);
		Assert.Equal("line 10", lines.Snapshot()[0].Text);
	}

	[Fact]
	public void LogLines_ClearStartsTheLogOver()
	{
		LogLines lines = new();
		lines.Append(Fixtures.LogTail);
		lines.Clear();
		lines.Append("2026-11-01T09:01:00Z after the clear\n");

		Assert.Equal(1, lines.Count);
		Assert.Equal(0, lines.Skipped);
		Assert.Equal("after the clear", lines.Snapshot().Single().Text);
	}
}
