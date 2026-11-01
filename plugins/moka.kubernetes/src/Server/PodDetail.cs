using System.Text.Json;

namespace Moka.Kubernetes.Server;

/// <summary>What one container of the pod is doing: waiting, running, or finished, and the detail kubectl gave.</summary>
public enum PodContainerState
{
	Unknown,

	/// <summary>Starting, pulling an image, or crashing and waiting to try again.</summary>
	Waiting,

	Running,

	/// <summary>Ended, well or badly; the detail says which and with what exit status.</summary>
	Terminated,
}

/// <summary>One container of the pod's spec and, when the status has it, what it is doing right now.</summary>
public sealed record PodContainer(
	string Name,
	string Image,
	bool Ready,
	int Restarts,
	PodContainerState State,
	string Detail)
{
	/// <summary>The state in one word for a chip or a column: waiting, running, terminated, unknown.</summary>
	public string StateWord => State switch
	{
		PodContainerState.Waiting => "waiting",
		PodContainerState.Running => "running",
		PodContainerState.Terminated => "terminated",
		_ => "unknown",
	};
}

/// <summary>One of the pod's conditions, such as Ready or PodScheduled, with the reason when it does not hold.</summary>
public sealed record PodCondition(string Type, string Status, string Reason, string Message);

/// <summary>
/// One pod's record as <c>kubectl get pod -o json</c> answers it, read by a small <see cref="JsonDocument"/> walk
/// that takes what is there and leaves what is not: a pod still scheduling has no node, and one that never started
/// has no conditions. Anything that is not this shape reads as null, because the words for why live with the run.
/// </summary>
public sealed record PodDetail(
	string Name,
	string Namespace,
	string Phase,
	string Node,
	string Ip,
	string QosClass,
	string StartTime,
	string RestartPolicy,
	IReadOnlyList<PodCondition> Conditions,
	IReadOnlyList<PodContainer> Containers)
{
	/// <summary>The names of every container in the pod's spec, in its order, for a log or exec picker.</summary>
	public IReadOnlyList<string> ContainerNames => [.. Containers.Select(container => container.Name)];

	public static PodDetail? Parse(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("metadata", out JsonElement metadata))
			{
				return null;
			}

			JsonElement spec = Object(root, "spec");
			JsonElement status = Object(root, "status");
			return new PodDetail(
				Text(metadata, "name"),
				Text(metadata, "namespace"),
				Text(status, "phase"),
				Text(spec, "nodeName"),
				Text(status, "podIP"),
				Text(status, "qosClass"),
				Text(status, "startTime"),
				Text(spec, "restartPolicy"),
				[.. ReadConditions(status).Select(condition =>
					new PodCondition(condition.Type, condition.Status, condition.Reason, condition.Message))],
				[.. ReadContainers(spec, status)]);
		}
		catch (JsonException)
		{
			return null;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
		catch (KeyNotFoundException)
		{
			return null;
		}
	}

	private static JsonElement Object(JsonElement parent, string name) =>
		parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement section) && section.ValueKind == JsonValueKind.Object
			? section
			: default;

	private static string Text(JsonElement parent, string name) =>
		parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString() ?? ""
			: "";

	private static IEnumerable<(string Type, string Status, string Reason, string Message)> ReadConditions(JsonElement status)
	{
		if (status.ValueKind != JsonValueKind.Object || !status.TryGetProperty("conditions", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
		{
			yield break;
		}

		foreach (JsonElement condition in list.EnumerateArray())
		{
			if (condition.ValueKind == JsonValueKind.Object)
			{
				yield return (Text(condition, "type"), Text(condition, "status"), Text(condition, "reason"), Text(condition, "message"));
			}
		}
	}

	private static IEnumerable<PodContainer> ReadContainers(JsonElement spec, JsonElement status)
	{
		if (spec.ValueKind != JsonValueKind.Object || !spec.TryGetProperty("containers", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
		{
			yield break;
		}

		foreach (JsonElement container in list.EnumerateArray())
		{
			if (container.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			string name = Text(container, "name");
			if (name.Length == 0)
			{
				continue;
			}

			PodContainerState state = PodContainerState.Unknown;
			string detail = "";
			bool ready = false;
			int restarts = 0;
			if (ContainerStatus(status, name) is { } counted)
			{
				ready = counted.Ready;
				restarts = counted.Restarts;
				state = counted.State;
				detail = counted.Detail;
			}

			yield return new PodContainer(name, Text(container, "image"), ready, restarts, state, detail);
		}
	}

	/// <summary>The status of the container with this name, or null when the pod's status does not say yet.</summary>
	private static (bool Ready, int Restarts, PodContainerState State, string Detail)? ContainerStatus(JsonElement status, string name)
	{
		if (status.ValueKind != JsonValueKind.Object || !status.TryGetProperty("containerStatuses", out JsonElement list) ||
			list.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		foreach (JsonElement counted in list.EnumerateArray())
		{
			if (counted.ValueKind != JsonValueKind.Object || Text(counted, "name") != name)
			{
				continue;
			}

			bool ready = counted.TryGetProperty("ready", out JsonElement readyValue) && readyValue.ValueKind == JsonValueKind.True;
			int restarts = counted.TryGetProperty("restartCount", out JsonElement restartValue) && restartValue.ValueKind == JsonValueKind.Number &&
				restartValue.TryGetInt32(out int count)
					? count
					: 0;
			if (Object(counted, "state") is { ValueKind: JsonValueKind.Object } state)
			{
				if (Object(state, "running") is { ValueKind: JsonValueKind.Object } running)
				{
					string started = Text(running, "startedAt");
					return (ready, restarts, PodContainerState.Running, started.Length > 0 ? $"since {started}" : "since an unstated time");
				}

				if (Object(state, "waiting") is { ValueKind: JsonValueKind.Object } waiting)
				{
					string reason = Text(waiting, "reason");
					string message = Text(waiting, "message");
					string detail = reason.Length > 0 ? reason : "waiting";
					if (message.Length > 0)
					{
						detail += $": {message}";
					}

					return (ready, restarts, PodContainerState.Waiting, detail);
				}

				if (Object(state, "terminated") is { ValueKind: JsonValueKind.Object } terminated)
				{
					string reason = Text(terminated, "reason");
					string exit = terminated.TryGetProperty("exitCode", out JsonElement exitValue) && exitValue.ValueKind == JsonValueKind.Number
						? exitValue.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture)
						: "?";
					return (ready, restarts, PodContainerState.Terminated, $"{(reason.Length > 0 ? reason : "ended")} (exit {exit})");
				}
			}

			return (ready, restarts, PodContainerState.Unknown, "");
		}

		return null;
	}
}
